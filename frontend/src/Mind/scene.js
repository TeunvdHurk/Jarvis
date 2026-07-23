import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js'
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js'
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js'

const STAR_COUNT = 600
const STAR_SHELL_MIN = 80
const STAR_SHELL_MAX = 160
const MEMBRANE_RADIUS = 26

/**
 * Builds the empty-but-atmospheric universe: renderer, camera, orbit controls,
 * bloom composer, starfield, membrane, fog. Returns { dispose, onResize } so the
 * Vue component can tear it down cleanly and hook into ResizeObserver.
 *
 * No node/edge data here on purpose (Tier 2) - regions.js/nodes.js/edges.js/data.js
 * are added in later tiers and consume this scene, they don't get folded into it.
 */
export function createMindScene(canvas) {
  const clock = new THREE.Clock()
  // Single shared time uniform object - every material that needs uTime references
  // this same object, so Tier 8's frame loop only writes it once per frame.
  const timeUniform = { value: 0 }

  // --- Renderer ---
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true })
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
  renderer.setSize(canvas.clientWidth, canvas.clientHeight, false)
  renderer.toneMapping = THREE.ACESFilmicToneMapping
  renderer.toneMappingExposure = 1.05
  renderer.setClearColor(0x05070b, 1)

  // --- Scene / fog ---
  const scene = new THREE.Scene()
  scene.fog = new THREE.FogExp2(0x05070b, 0.012)

  // --- Camera ---
  const camera = new THREE.PerspectiveCamera(
    55,
    canvas.clientWidth / Math.max(1, canvas.clientHeight),
    0.1,
    500
  )
  camera.position.set(0, 6, 34)

  // --- Orbit controls ---
  const controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true
  controls.dampingFactor = 0.06
  controls.autoRotate = true
  controls.autoRotateSpeed = 0.35
  controls.minDistance = 12
  controls.maxDistance = 70
  controls.target.set(0, 0, 0)

  // --- Bloom composer ---
  const composer = new EffectComposer(renderer)
  composer.addPass(new RenderPass(scene, camera))
  const bloomPass = new UnrealBloomPass(
    new THREE.Vector2(canvas.clientWidth, canvas.clientHeight),
    1.15, // strength
    0.6,  // radius
    0.72  // threshold
  )
  composer.addPass(bloomPass)

  // --- Starfield ---
  const starGeometry = new THREE.BufferGeometry()
  const starPositions = new Float32Array(STAR_COUNT * 3)
  const starPhases = new Float32Array(STAR_COUNT)
  const starSizes = new Float32Array(STAR_COUNT)

  for (let i = 0; i < STAR_COUNT; i++) {
    // Uniform-ish distribution on a shell between STAR_SHELL_MIN and STAR_SHELL_MAX.
    const radius = STAR_SHELL_MIN + Math.random() * (STAR_SHELL_MAX - STAR_SHELL_MIN)
    const theta = Math.random() * Math.PI * 2
    const phi = Math.acos(2 * Math.random() - 1)

    starPositions[i * 3 + 0] = radius * Math.sin(phi) * Math.cos(theta)
    starPositions[i * 3 + 1] = radius * Math.sin(phi) * Math.sin(theta)
    starPositions[i * 3 + 2] = radius * Math.cos(phi)

    starPhases[i] = Math.random() * Math.PI * 2
    starSizes[i] = 1.0 + Math.random() * 2.2
  }

  starGeometry.setAttribute('position', new THREE.BufferAttribute(starPositions, 3))
  starGeometry.setAttribute('aPhase', new THREE.BufferAttribute(starPhases, 1))
  starGeometry.setAttribute('aSize', new THREE.BufferAttribute(starSizes, 1))

  const starMaterial = new THREE.ShaderMaterial({
    uniforms: { uTime: timeUniform },
    transparent: true,
    depthWrite: false,
    blending: THREE.AdditiveBlending,
    vertexShader: /* glsl */ `
      uniform float uTime;
      attribute float aPhase;
      attribute float aSize;
      varying float vTwinkle;

      void main() {
        vec4 mv = modelViewMatrix * vec4(position, 1.0);
        // Twinkle biased so peaks cross the bloom threshold - that's what sparkles.
        vTwinkle = 0.55 + 0.45 * sin(uTime * 1.4 + aPhase);
        gl_PointSize = aSize * (140.0 / -mv.z);
        gl_Position = projectionMatrix * mv;
      }
    `,
    fragmentShader: /* glsl */ `
      varying float vTwinkle;

      void main() {
        vec2 uv = gl_PointCoord - vec2(0.5);
        float d = length(uv);
        if (d > 0.5) discard;
        float falloff = smoothstep(0.5, 0.0, d);
        vec3 color = vec3(0.85, 0.9, 1.0) * vTwinkle;
        gl_FragColor = vec4(color, falloff * vTwinkle);
      }
    `
  })

  const starfield = new THREE.Points(starGeometry, starMaterial)
  scene.add(starfield)

  // --- Membrane: faint fresnel-rim boundary sphere ---
  const membraneGeometry = new THREE.SphereGeometry(MEMBRANE_RADIUS, 48, 32)
  const membraneMaterial = new THREE.ShaderMaterial({
    uniforms: {
      uTime: timeUniform,
      uColor: { value: new THREE.Color(0x495db5) } // Jarvis accent, faint boundary tint
    },
    transparent: true,
    depthWrite: false,
    side: THREE.BackSide,
    blending: THREE.AdditiveBlending,
    vertexShader: /* glsl */ `
      varying vec3 vNormal;
      varying vec3 vViewDir;

      void main() {
        vNormal = normalize(normalMatrix * normal);
        vec4 mv = modelViewMatrix * vec4(position, 1.0);
        vViewDir = normalize(-mv.xyz);
        gl_Position = projectionMatrix * mv;
      }
    `,
    fragmentShader: /* glsl */ `
      uniform float uTime;
      uniform vec3 uColor;
      varying vec3 vNormal;
      varying vec3 vViewDir;

      void main() {
        // BackSide flips normals, so a plain max(dot(...),0.0) fresnel reads as a
        // solid disk instead of a rim - abs() is the fix, lights the silhouette
        // from inside and outside alike.
        float facing = abs(dot(normalize(vNormal), normalize(vViewDir)));
        float rim = pow(1.0 - facing, 3.0);
        float pulse = 0.85 + 0.15 * sin(uTime * 0.5);
        gl_FragColor = vec4(uColor, rim * 0.06 * pulse);
      }
    `
  })
  const membrane = new THREE.Mesh(membraneGeometry, membraneMaterial)
  scene.add(membrane)

  // --- Frame loop ---
  let rafId = null
  let disposed = false

  function tick() {
    if (disposed) return
    rafId = requestAnimationFrame(tick)
    timeUniform.value = clock.getElapsedTime()
    controls.update()
    composer.render()
  }
  tick()

  // --- Resize ---
  function onResize(width, height) {
    const w = Math.max(1, width)
    const h = Math.max(1, height)
    camera.aspect = w / h
    camera.updateProjectionMatrix()
    renderer.setSize(w, h, false)
    composer.setSize(w, h)
    bloomPass.setSize(w, h)
  }

  // --- Cleanup ---
  function dispose() {
    disposed = true
    if (rafId !== null) cancelAnimationFrame(rafId)
    controls.dispose()
    starGeometry.dispose()
    starMaterial.dispose()
    membraneGeometry.dispose()
    membraneMaterial.dispose()
    composer.dispose()
    renderer.dispose()
  }

  return {
    scene,
    camera,
    renderer,
    controls,
    composer,
    timeUniform,
    onResize,
    dispose
  }
}