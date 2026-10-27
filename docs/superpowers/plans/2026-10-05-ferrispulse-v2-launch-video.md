# FerrisPulse v2.0.0 官方 4K 电影级宣传片与交互舞台实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 打造 FerrisPulse v2.0.0 官方 4K 60FPS 电影级宣传动画（85秒、3840×2160），包含程序化 Web Audio 水晶声学引擎、4K 粒子与 3D 浮岛渲染舞台，并通过 Headless Chrome CDP + Jianying FFmpeg (NVENC) 离线压制导出极清 `promo/FerrisPulse_v2.0_Launch_4K.mp4`。

**Architecture:** 前端基于原生 HTML5 Canvas 2D / WebGL 搭建 4K 超清剧场与确定性虚拟时钟时间轴；音频采用 Web Audio API 程序化合成水晶八音盒与极客低频，支持 `OfflineAudioContext` 离线导出 44.1kHz WAV；后端基于 Node.js v24 原生 WebSocket CDP 协议驱动 Headless Chrome 逐帧步进渲染，通过标准输入流/帧缓存直连剪映 `ffmpeg.exe` NVENC 硬件编码器压制广播级 4K 60FPS MP4。

**Tech Stack:** HTML5 / Canvas 2D / Web Audio API / Node.js v24 (Native WebSocket & HTTP) / Chrome DevTools Protocol (CDP) / Jianying Pro FFmpeg (`h264_nvenc`, `aac_mf`).

**Spec:** `D:\电量管家\docs\superpowers\specs\2026-10-05-ferrispulse-v2-launch-video-design.md`

## Global Constraints

- **Resolution & Frame Rate:** 严格 3840×2160 UHD @ 60.0 FPS，总时长 85 秒（共 5100 帧）。
- **Visual Style:** “静谧致雅双轨融合风”（前 60s 凌厉极客科技脉冲 ➔ 后 25s 极光漫融与诗意共鸣）。
- **Hardware Lineup:** 核心展示 5 款专属外设：`Razer DeathAdder V4 Pro`（炼狱蝰蛇 V4 Pro）、`Logitech GPW 3 代`、`Rapoo VT9 PRO`、`NuPhy WH80 三模磁轴键盘`（电竞磁轴 RT · WinLock 状态）、`Samsung Galaxy Buds 4 Pro`（左/右/仓独立电量）。
- **Implicit Ecosystem:** 5 卡片后无文字无声展开数十节点外设星图网络，镜头 Orbit Zoom 暗示全品类支持。
- **Zero External Asset Dependency:** 音效全由 Web Audio API 程序化合成，字体使用系统 Win11 字体栈与 CSS 矢量绘制，杜绝网络资源加载失败。
- **Dual Platforms:** 官方仓库统一为 `GitHub: Ferris-echo/FerrisPulse` 与 `Gitee: Ferris-echo/ferris-pulse`。
- **Hardware Acceleration:** FFmpeg 压制优先调用本地 NVIDIA 硬件编码器 `h264_nvenc`，音频使用 `aac_mf`，像素格式 `-pix_fmt yuv420p`。
- **Closed Source Safety:** 所有宣传片代码与产物均保存在本地 `D:\电量管家\promo` 与 `tools`，绝不向公开仓库泄露 C# 客户端业务源码。

## Review Focus

1. **DPI 缩放失真:** Windows 系统 125%/150% 缩放会导致 Headless Chrome 视口变形，必须在 CDP 启动参数与 `Emulation.setDeviceMetricsOverride` 强制锁定 `deviceScaleFactor: 1.0`，宽 3840、高 2160。
2. **离线音频爆音与对齐:** `OfflineAudioContext` 导出的 85 秒 PCM 数据在拼接或包络释放时不得有 DC 偏置或削波爆音，WAV 头部需符合标准 RIFF 44.1kHz 16-bit 立体声规范，与 5100 帧画面严格对齐。
3. **海量 4K 帧显存/磁盘压力:** 5100 张 4K PNG 若一次性存盘会占用 ~25GB，实施中采用批量管道流式推送到 FFmpeg 或按幕分批处理并自动清理临时缓存，保证稳定运行。
4. **字体异步加载闪烁:** 序幕与终章涉及华文楷体与 HarmonyOS 字体，必须在第一帧渲染前 `await document.fonts.ready`，防止首帧文字跳动。
5. **NVENC 参数兼容性:** `h264_nvenc` 需配置 `-cq 19 -b:v 35M -maxrate 50M -bufsize 50M` 保证 B 站 4K 60FPS 极清广播级画质，同时若显卡负载异常可平滑回退至 `h264_mf`。

---

### Task 1: Web Audio 程序化声学合成引擎与无损 WAV 导出器

**Files:**
- Create: `promo/audio.js`
- Test: `tools/test_audio.mjs`

**Interfaces:**
- Consumes: Web Audio API (`AudioContext`, `OfflineAudioContext`, `OscillatorNode`, `GainNode`, `ConvolverNode`)
- Produces:
  - `class PromoAudioEngine`:
    - `initInteractive(): Promise<void>` (初始化交互式实时播放)
    - `playCue(cueName: string, timeSec?: number): void` (触发特定分镜音效)
    - `generatePromoAudioWav(durationSec?: number, sampleRate?: number): Promise<ArrayBuffer>` (离线导出立体声 WAV 二进制数据)

- [ ] **Step 1: 编写离线音频生成与 WAV 格式测试用例 `tools/test_audio.mjs`**

```javascript
// tools/test_audio.mjs
import assert from 'node:assert';
import { PromoAudioEngine } from '../promo/audio.js';

async function run() {
  const engine = new PromoAudioEngine();
  // 测试生成 3 秒离线音频片段
  const wavBuffer = await engine.generatePromoAudioWav(3.0, 44100);
  assert(wavBuffer instanceof ArrayBuffer, 'Must return ArrayBuffer');
  assert(wavBuffer.byteLength > 44, 'Must be larger than WAV header');
  const view = new DataView(wavBuffer);
  // 校验 RIFF 标识
  const riff = String.fromCharCode(view.getUint8(0), view.getUint8(1), view.getUint8(2), view.getUint8(3));
  assert.strictEqual(riff, 'RIFF');
  console.log('Task 1 Test PASSED: Audio engine generates valid RIFF WAV header and data');
}
run();
```

- [ ] **Step 2: 运行测试验证失败（文件尚未创建）**

Run: `node tools/test_audio.mjs`
Expected: FAIL with "Cannot find module '../promo/audio.js'"

- [ ] **Step 3: 实现 `promo/audio.js`**

包含：
1. 水晶钢片琴音色合成器（水晶半音泛音列，基于双正弦调制与指数包络衰减，还原软件内 44.1kHz 水晶音阶）；
2. 科技脉冲与 Sub-bass 低频滑音合成器；
3. 空间混响合成（使用人工冲激响应拟真算法生成平滑混响尾音）；
4. 四幕分镜完整乐曲时间轴编排（序幕微光脉冲 ➔ 第一幕五设备水晶泛音阶 ➔ 第二幕暗黑律动 ➔ 第三幕高频电竞雷达蜂鸣 ➔ 第四幕空灵极光和弦与心语八音盒）；
5. `writeWavFile(audioBuffer)` 导出标准 16-bit PCM 立体声 RIFF WAV。

- [ ] **Step 4: 运行测试验证通过**

Run: `node tools/test_audio.mjs`
Expected: PASS with "Task 1 Test PASSED"

- [ ] **Step 5: 暂存并记录产物状态**

---

### Task 2: 4K 电影级画布渲染引擎与分镜编排核心

**Files:**
- Create: `promo/renderer.js`
- Test: `tools/test_renderer.mjs`

**Interfaces:**
- Consumes: HTML5 Canvas 2D Context (3840×2160)
- Produces:
  - `class PromoRenderer`:
    - `constructor(canvas: HTMLCanvasElement)`
    - `setSeekFrame(frameIndex: number): void` (设置确定性帧时间轴)
    - `renderFrame(frameIndex: number): void` (渲染指定帧到 4K Canvas)
    - `getTotalFrames(): number` (返回 5100 帧)
    - `getCurrentScene(frameIndex: number): object` (返回当前场景元信息)

- [ ] **Step 1: 编写 Canvas 渲染引擎离线单帧与剧本时序测试 `tools/test_renderer.mjs`**

```javascript
// tools/test_renderer.mjs
import assert from 'node:assert';
import { PromoRenderer } from '../promo/renderer.js';

function run() {
  const fakeCanvas = {
    width: 3840,
    height: 2160,
    getContext: () => ({
      fillRect: () => {},
      clearRect: () => {},
      save: () => {},
      restore: () => {},
      beginPath: () => {},
      arc: () => {},
      fill: () => {},
      stroke: () => {},
      fillText: () => {},
      measureText: () => ({ width: 100 }),
      createLinearGradient: () => ({ addColorStop: () => {} }),
      createRadialGradient: () => ({ addColorStop: () => {} })
    })
  };
  const renderer = new PromoRenderer(fakeCanvas);
  assert.strictEqual(renderer.getTotalFrames(), 5100);
  // 测试序幕、第一幕、第二幕、第三幕、第四幕
  assert.strictEqual(renderer.getCurrentScene(300).name, 'PROLOGUE');
  assert.strictEqual(renderer.getCurrentScene(1000).name, 'ACT_1_ECOSYSTEM');
  assert.strictEqual(renderer.getCurrentScene(2000).name, 'ACT_2_AESTHETICS');
  assert.strictEqual(renderer.getCurrentScene(3500).name, 'ACT_3_GAMING');
  assert.strictEqual(renderer.getCurrentScene(4500).name, 'ACT_4_AURORA_POEM');
  console.log('Task 2 Test PASSED: Renderer timeline and scenes are accurately configured');
}
run();
```

- [ ] **Step 2: 运行测试验证失败**

Run: `node tools/test_renderer.mjs`
Expected: FAIL with "Cannot find module '../promo/renderer.js'"

- [ ] **Step 3: 实现 `promo/renderer.js`**

包含：
1. **粒子物理引擎**：
   - 爆炸重组 (Shatter & Reassemble)：1.0 鼠标轮廓粒子化，旋聚为 2.0 钛金徽标；
   - 星尘漂浮 (Stardust) 与极光帘幕 (Aurora Waves)；
   - 拟真水面双环波纹 (Dual Ring Interference)。
2. **第一幕 · 3D 浮岛设备卡片绘制器与浩瀚星图**：
   - 雷蛇 DeathAdder V4 Pro (98% ⚡ 翡翠绿流光充电流)；
   - 罗技 GPW 3 代 (85% 琥珀金呼吸待机)；
   - 雷柏 VT9 PRO (76% 纯净电量环)；
   - NuPhy WH80 (三模磁轴键盘 · Rapid Trigger 触发行程 · WinLock 独立状态)；
   - **三星 Galaxy Buds 4 Pro** (优雅三环独立电量：左耳 L 90%、右耳 R 85%、电池仓 Case 100% ⚡)；
   - 浩瀚外设宇宙星图（镜头 Orbit 拉升，5 卡片为锚点，点亮多外设剪影与脉冲星座网络，画面无声暗示全品牌支持）。
3. **第二幕 · 自研手造暗黑美学与托盘变形**：
   - 底层 GDI+ 矢量精雕暗黑主界面与卡片微晶投影；
   - Cyber Pulse 能量扫波；
   - 托盘三形态（胶囊式、原生微标、极简数字）毫秒级平滑变形切换。
4. **第三幕 · 硬核电竞 OSD 与 NKRO 测速雷达**：
   - 8000Hz 回报率激活 HUD；
   - 击键并发雷达图动态跳动；
   - 局域网/云端 SWR 毫秒热重载提示。
5. **第四幕 · 极光漫融、华文楷体香槟金诗意共鸣**：
   - 《写在代码之外》心语逐行错峰浮现：
     * “从最初的一行代码，到如今的万行重构。”
     * “每一个像素的呼吸，都是我们与热爱对齐的频率。”
     * “愿这束灵脉微光，陪你赢下每一场深夜的战局。”
     * “—— Ferris-echo × 老G 联合呈现”
   - 终章 FerrisPulse 官方定格微标。
6. **虚拟时钟导出挂钩**：绑定 `window.renderVideoFrame` 与 `window.getVideoTotalFrames`。

- [ ] **Step 4: 运行测试验证通过**

Run: `node tools/test_renderer.mjs`
Expected: PASS with "Task 2 Test PASSED"

---

### Task 3: 4K 响应式影院发布舞台与自由交互界面

**Files:**
- Create: `promo/index.html`
- Create: `promo/style.css`
- Test: `tools/test_stage.mjs` (无头浏览器加载与样式无错测试)

**Interfaces:**
- Consumes: `promo/audio.js`, `promo/renderer.js`
- Produces:
  - 网页舞台：原生 3840×2160 视口，纯 CSS 等比缩放适配任意显示器（1080p/2K/4K/带鱼屏）；
  - 影院控制栏：播放/暂停、精准时间轴拖拽（00:00 - 01:25）、全屏、静音/解禁、4K 60FPS 极清金色徽标、5 大场景快速跳转跳轨器；
  - 自由探索模式：支持鼠标滑动悬停查看 5 款设备参数、点击触发涟漪水波纹。

- [ ] **Step 1: 编写网页舞台集成测试脚本 `tools/test_stage.mjs`**

通过 Node.js 启动 Chrome Headless 访问 `promo/index.html`，检测：
1. 页面无任何 JavaScript 报错或 404 资源；
2. Canvas 宽高锁定为 3840×2160；
3. `window.renderVideoFrame` 与 `window.getVideoTotalFrames` 存在且可用。

- [ ] **Step 2: 运行测试验证失败**

Run: `node tools/test_stage.mjs`
Expected: FAIL (页面文件不存在)

- [ ] **Step 3: 实现 `promo/index.html` 与 `promo/style.css`**

- [ ] **Step 4: 运行测试验证通过**

Run: `node tools/test_stage.mjs`
Expected: PASS with "Stage loaded successfully with 3840x2160 canvas"

---

### Task 4: 离线 4K CDP 逐帧捕获与 FFmpeg NVENC 压制管线

**Files:**
- Create: `tools/render_promo_video.mjs`
- Test: `tools/render_promo_video.mjs --preview` (快速抽检 5 个关键帧并生成 3 秒 4K 试看片)

**Interfaces:**
- Consumes:
  - Chrome 可执行文件: `C:\Program Files\Google\Chrome\Application\chrome.exe`
  - 剪映 FFmpeg: `E:\Program Files (x86)\JianyingPro\11.5.0.14471\ffmpeg.exe`
  - Node.js 内置模块: `child_process`, `fs`, `path`, `http`, `WebSocket`
- Produces:
  - `promo/dist/promo_audio.wav` (85秒 44.1kHz 立体声母带)
  - `promo/dist/frames/frame_%05d.png` (4K UHD 帧缓冲区，流式处理)
  - `promo/FerrisPulse_v2.0_Launch_4K.mp4` (终极 4K 60FPS 成品视频)

- [ ] **Step 1: 编写压制管线预览抽检模式**

实现 `--preview` 参数：
- 仅捕获 0s (帧 0)、15s (帧 900)、35s (帧 2100)、55s (帧 3300)、75s (帧 4500) 5 个关键帧，保存为 PNG 校验视觉；
- 压制前 3 秒 (180 帧) 4K 60FPS 试看视频 `promo/dist/preview_3s_4K.mp4`，验证 NVENC 编码器与音频合成管道。

- [ ] **Step 2: 运行 `--preview` 模式验证**

Run: `node tools/render_promo_video.mjs --preview`
Expected: 生成 5 张 4K 关键帧与 `preview_3s_4K.mp4`，控制台打印 NVENC 编码成功。

- [ ] **Step 3: 检查关键帧画面质感与参数**

确认：
1. 分辨率精确为 3840×2160；
2. 三星 Galaxy Buds 4 Pro 三卡电量展示正常；
3. 钛金微标与极光质感正常。

---

### Task 5: 全程 5100 帧 4K 广播级渲染、MP4 压制与交付验收

**Files:**
- Produces: `promo/FerrisPulse_v2.0_Launch_4K.mp4`
- Test: `tools/verify_video.mjs`

- [ ] **Step 1: 编写视频参数自动化校验脚本 `tools/verify_video.mjs`**

使用 FFmpeg / FFprobe 提取媒体流元数据，断言：
1. 宽度 = 3840，高度 = 2160；
2. 帧率 = 60.0 fps；
3. 时长 >= 84.5s 且 <= 85.5s；
4. 包含 AAC 立体声音轨；
5. 文件大小正常 (> 80MB)。

- [ ] **Step 2: 执行全量 4K 60FPS 渲染导出**

Run: `node tools/render_promo_video.mjs --full`
监控实时帧率、进度百分比与 NVENC 编码速度。

- [ ] **Step 3: 运行视频完整性检验脚本**

Run: `node tools/verify_video.mjs`
Expected: PASS with "FerrisPulse_v2.0_Launch_4K.mp4 satisfies all 4K broadcast specifications"

- [ ] **Step 4: 交付与播放指引**

为用户提供本地直接双击播放路径，并提供浏览器交互式全屏舞台访问方式。
