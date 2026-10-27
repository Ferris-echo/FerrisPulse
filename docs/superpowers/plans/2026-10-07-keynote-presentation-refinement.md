# FerrisPulse 2.0 苹果发布会级互动演播系统优化实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 深度重构 FerrisPulse 2.0 演示网页（HTML/CSS/JS）及全流程定制 BGM，解决图片色阶断层、文字折行、序幕过长问题，融入双卡片电影级景深拉焦互换与诗句星芒逐行凝聚动效，实现开箱即用的高品质全屏录屏体验。

**Architecture:** 
纯原生前端架构（HTML5 + CSS 3D Transforms/Web Animations + ES6 Canvas/Web Audio API），零外部构建依赖。通过无损 Sidechain 算法合成高品质立体声 BGM；重构 CSS 阴影模型消除 8-bit 色阶；为 DOM 状态机增添多卡片景深拉焦（Rack-Focus）与诗句时序阶梯控制器。

**Tech Stack:**
- 音频合成：Node.js 原生 44.1kHz 16-bit Stereo PCM WAV 合成器（带 Sidechain Ducking、Multiband Filter Sweep 与多振荡器合唱）
- 前端核心：HTML5, CSS3 (3D Preserve-3D, Backdrop-filter, CSS Custom Properties, Viewport Units), Vanilla JavaScript ES6
- 资源与截图：`promo/assets/screenshots/` (100% 真实纯黑沉浸式 Windows 11 实拍截图)

---

## 全局约束与规范 (Global Constraints)
1. **零外部依赖**：`presentation.html` 必须能直接离线双击打开即用，禁止引入任何 CDN、外部字体或三方脚本。
2. **零色阶断层**：严禁在深黑（`#08080c`）背景上使用多重有色 `box-shadow` 发光，阴影严格采用纯中性物理遮蔽与 1px 微光边框。
3. **左侧说明文字常驻清晰**：放大图片与卡片交互时，左侧说明文字与参数列表始终保持 100% 锐利可读，绝不进行虚化或暗化。
4. **全屏录屏友好**：开始播放后，顶部导航栏与控制按钮自动平滑淡出，全屏画面 100% 纯净无 UI 遮挡。

---

## 任务拆解与分步执行清单

### Task 1: 第二页（Chapter 1 痛点页）卡片宽度与换行优化
**Files:**
- Modify: `promo/presentation.css`
- Modify: `promo/presentation.html`

**需求目标:**
解决用户实机截图中“单一原生托盘聚合全\n生态”和“游戏模式按需智能调\n度”末尾折断 1~2 个字的问题，使卡片内容自然单行舒展。

- [ ] **Step 1: 扩大对比胶囊卡片宽度并优化字间距与排版**
  - 在 `promo/presentation.css` 中将 `.feature-capsule-card` 的宽度由当前限制调整为 `width: 100%; max-width: 660px; min-width: 580px;`。
  - 在 `.comparison-list li` 中添加 `white-space: nowrap;` 或微调 `font-size: 0.95rem;` 与 padding，确保在 1080p 及不同缩放下文本绝不突兀折断单字。
- [ ] **Step 2: 验证文字排版**
  - 在浏览器或无头截图中查看 Chapter 1 右侧卡片，确认三条对比文案均呈现完美的单行舒展状态。

---

### Task 2: 根除图片光晕色阶断层，重塑 Apple Pro 物理视窗质感
**Files:**
- Modify: `promo/presentation.css`

**需求目标:**
彻底根除暗黑背景上有色光晕产生的严重同心圆色阶断层（Banding），打造纯净通透的高级视窗质感。

- [ ] **Step 1: 清除所有有色大半径外发光 box-shadow**
  - 清理 `.razer-border`, `.nuphy-border`, `.purple-border`, `.danger-border`, `.pulled-stage` 等类中的 `rgba(..., 0.14)` 大半径发光层。
- [ ] **Step 2: 构建纯中性物理深阴影与 1px 精细微光倒角**
  - 核心阴影改为多层中性深邃遮蔽：
    ```css
    box-shadow: 
      0 36px 90px rgba(0, 0, 0, 0.88),
      0 12px 28px rgba(0, 0, 0, 0.65),
      inset 0 1px 0 rgba(255, 255, 255, 0.16);
    ```
  - 外设专属品牌色（雷蛇绿、NuPhy蓝、Xbox紫、电竞红）仅作为 1px 边框的细腻高光反射（`border-color: rgba(...)`），杜绝扩散到背景产生色阶。

---

### Task 3: 结合方案一与方案二：双卡片电影级景深拉焦与长镜头推拉
**Files:**
- Modify: `promo/presentation.css`
- Modify: `promo/presentation.js`

**需求目标:**
- 方案一：在 Chapter 2（向导 vs 优先级）与 Chapter 4（游戏守护 vs 8000Hz 浮窗）中，当卡片 A 抽出放大时，卡片 B 自然退入景深失焦（`blur(6px)`, 微缩, 35% 亮度）；卡片 B 抽出时焦点反转。左侧文字始终清晰。
- 方案二：全片翻页时，旧章节沿 Z 轴向远方推远并柔和散焦，新章节从景深深处清晰对焦。

- [ ] **Step 1: 在 CSS 中实现双卡片景深焦点拉焦样式**
  - 为 `.dual-stacked.focus-wizard` 设置 `#cardWizard` 锐利对焦、`#cardPriority` 退入景深虚化（`filter: blur(6px); opacity: 0.35; transform: ...`）。
  - 为 `.dual-stacked.focus-priority` 设置反转拉焦：`#cardPriority` 锐利对焦、`#cardWizard` 退入景深虚化。
  - 为 Chapter 4 游戏组合卡片应用相同的大光圈景深互换。
- [ ] **Step 2: 保持左侧文字 100% 锐利**
  - 彻底移除 `.text-pane.dimmed-for-zoom`，确保文字在整场演示中始终保持清爽、无任何模糊与变暗。
- [ ] **Step 3: 优化翻页电影级长镜头景深过渡**
  - 在 `.chapter-slide` 与 `.chapter-slide.past`, `.future` 之间微调平滑贝塞尔曲线与散焦值，实现连贯推拉体验。

---

### Task 4: 序幕缩短至 3 秒与顶部按钮播放自隐交互
**Files:**
- Modify: `promo/presentation.html`
- Modify: `promo/presentation.js`
- Modify: `promo/presentation.css`

**需求目标:**
- 第一页时长缩短至 3.0s；
- 播放演示时顶部导航栏（包括右上角 4 个控制按钮）自动平滑淡出，提供 100% 纯净录屏画面；鼠标移至顶部或按暂停时恢复显示；
- 最后一页彻底移除 `#btnReplay` 重新开始按钮。

- [ ] **Step 1: 更新 `presentation.html`**
  - 删除 Chapter 9 结尾的 `<button id="btnReplay" ...>`。
- [ ] **Step 2: 在 `presentation.css` 中增加播放时控制栏自隐逻辑**
  - 当 `body.is-presenting` 时，`.keynote-header` 设置 `opacity: 0; transform: translateY(-100%); transition: all 0.5s ease;`。
  - 当鼠标 hover 到顶部屏幕 60px 区域（或移除 presenting 状态）时平滑淡入。
- [ ] **Step 3: 更新 `presentation.js` 播放控制与时序**
  - 第一页时间节点调整为：0.0s 开始 ➔ **3.0s 顺畅翻至 Chapter 1**。
  - 启动自动播放时自动添加 `is-presenting` 类，暂停或结束时移除。

---

### Task 5: 尾部致敬诗句“星芒聚焦雾化逐行凝聚”
**Files:**
- Modify: `promo/presentation.css`
- Modify: `promo/presentation.js`

**需求目标:**
诗句按朗读节奏逐行阶梯推进，采用 Blur-to-Focus（从虚化晨雾到凝聚聚焦）与字间距呼吸收束动效，第 3 句赋予翡翠向冷银流光扫光。

- [ ] **Step 1: 在 CSS 中构建 `.poem-line` 聚焦动画与初始雾化状态**
  - 每一行初始：`opacity: 0; filter: blur(14px); transform: translateY(20px); letter-spacing: 0.16em;`。
  - 激活类 `.revealed`：`opacity: 1; filter: blur(0); transform: translateY(0); letter-spacing: 0.04em; transition: all 1.2s cubic-bezier(0.16, 1, 0.3, 1);`。
  - 第 3 句高潮赋予柔和金属扫光效果。
- [ ] **Step 2: 在 JS 时间线中为 Chapter 9 添加逐行时序触发器**
  - Line 1 ➔ +2.0s Line 2 ➔ +2.2s Line 3 ➔ +1.8s 落款 ➔ +1.5s 开源徽章。

---

### Task 6: 重写全流程定制 BGM：动感、好听、Sidechain 呼吸律动
**Files:**
- Modify: `tools/generate_keynote_bgm.mjs`
- Generate: `promo/custom_keynote_bgm.wav`

**需求目标:**
彻底告别单调的正弦波循环，采用现代影视科技电音架构（带 Sidechain 动态抽吸、多振荡器合唱、滤波扫频、层次分明的 808 踢鼓与清脆踩镲）。

- [ ] **Step 1: 重写 `tools/generate_keynote_bgm.mjs`**
  - 算法引入 Sidechain 动态衰减函数（踢鼓落下瞬间 Pad/Bass 柔和收缩并弹性回弹）；
  - 序幕 3 秒简短空灵引子 ➔ 痛点科技节奏 ➔ 生态明朗律动大调 ➔ 8000Hz 科技燃点 Supersaw 齐奏 ➔ 尾部极光悠扬淡出；
  - 整体旋律抓耳悦耳、声场开阔不单调。
- [ ] **Step 2: 重新合成母带**
  - 运行脚本生成全新 `promo/custom_keynote_bgm.wav`。

---

### Task 7: 端到端本地全屏体验与无头截帧验收
**Files:**
- Verify: `promo/presentation.html`
- Verify: `promo/presentation.css`
- Verify: `promo/presentation.js`

- [ ] **Step 1: 抽样验证 Chapter 1 文本框宽度**（确认“生态”、“度”不再换行）。
- [ ] **Step 2: 抽样验证 Chapter 2 景深双卡片**（确认向导对焦时优先级卡片柔和虚化，文字 100% 锐利）。
- [ ] **Step 3: 抽样验证 Chapter 9 诗句逐行凝聚**（确认星芒聚焦动效完美）。
- [ ] **Step 4: 抽样验证暗部阴影**（确认完全无环形色阶断层）。
