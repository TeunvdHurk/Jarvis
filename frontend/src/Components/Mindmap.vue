<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { createMindScene } from '../mind/scene.js'

const canvasRef = ref(null)
const containerRef = ref(null)

let mindScene = null
let resizeObserver = null

onMounted(() => {
  mindScene = createMindScene(canvasRef.value)

  resizeObserver = new ResizeObserver((entries) => {
    for (const entry of entries) {
      const { width, height } = entry.contentRect
      mindScene.onResize(width, height)
    }
  })
  resizeObserver.observe(containerRef.value)
})

onUnmounted(() => {
  resizeObserver?.disconnect()
  mindScene?.dispose()
  mindScene = null
})
</script>

<template>
  <div ref="containerRef" class="mind-root">
    <canvas ref="canvasRef" class="mind-canvas"></canvas>
    <div class="mind-hud">
      <span class="mind-hud__title">JARVIS</span>
      <span class="mind-hud__sub">mind map — tier 2</span>
    </div>
  </div>
</template>

<style scoped>
.mind-root {
  position: relative;
  width: 100vw;
  height: 100vh;
  background: #05070b;
  overflow: hidden;
}

.mind-canvas {
  display: block;
  width: 100%;
  height: 100%;
}

.mind-hud {
  position: absolute;
  top: 20px;
  left: 24px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  pointer-events: none;
  color: #e6e9f0;
  text-shadow: 0 1px 6px rgba(0, 0, 0, 0.6);
  user-select: none;
}

.mind-hud__title {
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.28em;
  color: #a9b2ff;
}

.mind-hud__sub {
  font-size: 11px;
  letter-spacing: 0.05em;
  color: #6b7280;
}
</style>