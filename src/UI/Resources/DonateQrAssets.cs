// ============================================================================
// 模块名称: DonateQrAssets.cs
// 所属区域: UI/Resources (静态资源支持层)
// 核心职责:
//   提供赞赏模块专属的电竞艺术质感二维码图像资源（微信支付 & 支付宝）。
//   左侧微信采用翡翠暗绿与微晶底板，纯净圆角几何点阵，零头像无杂乱图标；
//   右侧支付宝采用深邃电光蓝与冰晶底板，纯净圆角几何点阵，零头像无杂乱图标。
//   完全由软件设计语言原生绘制，极简克制高级。
//   双重保障机制：优先从磁盘 resources 目录安全加载，若独立便携单文件运行
//   则自动回退至高保真内嵌 Base64 解码，确保 100% 便携与离线零缺失。
// 依赖关系: 无外部依赖 (GDI+ MemoryStream)
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.IO;

namespace RazerBatteryTray
{
    public static class DonateQrAssets
    {
        private static Image cachedWeChatQr = null;
        private static Image cachedAlipayQr = null;
        private static readonly object lockObj = new object();

        private const string WeChatQrBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAdQAAAHUCAYAAACDJ9lsAAAfv0lEQVR4nO3csY4c2XmG4RqLEnZtQhDATIGVCSbgCyDAxIByYmEFViL4HhQpI+VIEW/CiRzQ" +
            "WGxuwMkQvALSYGYHmw0wMMbahYYCHRiEZ7jc7q6p81d9p87zpKRap06dqh/dC75nV3/64/sJAFjkL7ZeAADsgYEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQ" +
            "gIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQ" +
            "gIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQgIEKAA0YqADQwL2tF/Cx+//6ZOsl7MrV339V+vn3zn4w/ff1/0y//49/mf7wX/8+ff3txa0//+lnD6Zf/fXf" +
            "Tb/9m3+YfvzDv5revf/zUOuZK2391es59vlzpd9f77e2qt9vc51d/emP77dexE0OXFvVB+7q3TfTF+fPplcXbw7+vUcPHk5fPn423b/3+VDrmStt/dXrOfXz" +
            "50q9v95vbaUNVD/5ssjv3/xhenXxZrp39oPv/Tv3zn4wvbp4M/3+zR+GW89caeuvXs8pnz9X8v1l3wxUFvnn//y36Ww6O/jT2rv3f57OprPpn//z34Zbz1xp" +
            "669ezymfP1fy/WXf4v4bKn25+NN/n/T33k/vT/67S6StZ6609Vevp+oaUu8v++YbKgA0YKACQAMGKgA0YKCyyIMf/Xg6m86O/r2z6Wx68KMfr7AiWqq+v6d+" +
            "PvTAQGWRX//sF9P76f3Rf1bxfno//fpnv1hxZbRQfX9P+XzohYHKIr99+Kvp0YOHR/9ZxaMHD6ffPvzViiujher7e8rnQy8MVBb5yQ/vT189/t30m5//cvrp" +
            "Zw++8+c//ezB9Juf/3L66vHvpp/88P4GK2SJ6vt77POhJ92nB9PSU9V635/qNmza/rRu1X5s7vq3bvOm39/e19+73vdH2IFVXV5fHWy3fv3txfT87Yvp5cXr" +
            "yBbrXMeud23V+9/7/e19/WzLT76sKq1VW62iVbtEQps3+f72vn62ZaCyqrRWbbWKVu0SCW3e5Pvb+/rZlp98WVVaq7Za2jWktHlT72/v62dbvqECQAMGKgA0" +
            "YKACQAMGKqtKacP23ha2P5DHQGVVCW3Y3tvC9gcyGaisKqEN23tb2P5AJgOVVW3dhu29LWx/INdw/w51biuyWlyLsrhl+u79n6e/vPfZ9E9/+4/TP/3tPx79" +
            "u3NVf35Sm3eN/am+3rTnsXdp+5n2fqs23EDlMC3Tw9LavNVGu15Ywk++3KJlelham7faaNcLSxio3KJlelham7faaNcLS/jJl1u0TA8b7ZpHu15YwjdUAGjA" +
            "QAWABgxUAGjAQIUZTm3hzpXazq263lRaxyxhoMIMp7Rw50pu51ZcbzKtY5YwUGGGU1q4cyW3cyuuN5nWMUsYqDDDsRbuXOnt3NbXm07rmCX8O1QWSWuHHrNm" +
            "i3iuNb4F3qXVXHW9dzH3vB37+0uud5Rv7ZzON1SG8qFF/OT86XR5fbX1clZ3eX01PTl/Oj1/++KTofvR9me066WWgcpwtIi1mm8a7XqpY6AyHC1ireabRrte" +
            "6vhvqAxJi/iw0fZntOulhm+oANCAgQoADRioANCAgQp0Y7S2MH0xUIFujNYWpi8GKtCN0drC9MVABboxWluYvvh3qCxy9fdfzfr7c1us1Z8/Wou42rFW8FxL" +
            "28JrtH/hA99QoSPp7dljreC50q43bT1kMVChM8nt2VNawXOlXW/aeshhoEJnktuzp7SC50q73rT1kMN/Q4UOpbZnq9aUdr1p6yGDb6gA0ICBCgANGKgA0ICB" +
            "yqpObbGeTWfTgx/9eIUV9emu+1O9/2mt3ar1OJ98ioHKqk5psd47+8H0fno//fpnv1hxZf1Ysj/V+5/W2q1Yj/PJ9zFQWdUpLdZ37/88PXrwcPrtw1+tuLJ+" +
            "LNmf6v1Pa+1WrMf55PsYqKzqWIv1p589mH7z819OXz3+3fSTH97fYIXZlu5P9f6ntXZbr8f55JCzqz/98f3Wi7ipuvXau7QW7jFL26et27AfS2sRz3Vsf9Lb" +
            "s2nnrVra+UnT+/74hkqppe3T1m3YvTm2P6O1Z0e7XrIYqJRb0j6taMPuySn7M1p7drTrJYeBSrkl7dOKNuyenLI/o7VnR7tecmj5soq7tk/1Ug87dX9Ga8+O" +
            "dr1k8A0VABowUAGgAQMVABowUFnFXtqwaXpvI7u/7ImBSrk9tWHT9N5Gdn/ZEwOVcntqw6bpvY3s/rInBiql9taGTdN7G9n9ZU+6b/lyWFqrtro9m9Y6Ttuf" +
            "3j+/9/33fmsrreUr7MCqLq+vpi/On02vLt588s8/tFhfXryevnz8bLp/7/OVV7it6v3p/fOr9b5+tuUnX1alPXtY9f70/vnVel8/2zJQWZX27GHV+9P751fr" +
            "ff1sy0++rEp79rDq/en986v1vn625RsqADRgoAJAAwYqADRgoLIrvbdte9f7/ve+frZloLIrvbdte9f7/ve+frZloLIrvbdte9f7/ve+frZloLIrvbdte9f7" +
            "/ve+frYV1/JlW2kt3GOWtlXnStuf3vf/WDt3qbTWK/vmGypd+9BWfXL+dLq8vtp6OcNZuv+X11fTk/On0/O3L5oPU1ibgUr3tFW3Vd3+hV4YqHRPW3Vb1e1f" +
            "6IWWL7ugrbqt6vYv9MA3VABowEAFgAYMVABowEDlFi3TbVXv/6mfP5fzAAYqH9Ey3Vb1/p/y+XM5D/B/DFRu0TLdVvX+n/L5czkP8H8MVG7RMt1W9f4f+/y5" +
            "nAf4f3Et32Ntz/R26NbWbtvO1bol+7G57dbeW7LVz0vr9ayt9Xnw/jks/f1TLW6gXr37Zvri/Nn06uLNwb/36MHD6cvHz6b79z4v+fze3XV/qqUN1OrzUD1Q" +
            "q5+XqvWspeo8eP8clvr+qRb3k+8pbU/t0MO0bU/X+3mofl4q1pPM+2e5kd8/cQP1lLanduhh2ran6/08VD8vFetJ5v2z3Mjvn7iW76ltT+3Qw7RtT9P7HlU/" +
            "L3PZzzaf37tR3z9x31ABoEcGKgA0YKACQAPDDdSqlmma1LbqKPtPn7Ss2xh1f4YbqBUt0zTJbdUR9p9+aVkvN/L+DDdQK1qmaZLbqiPsP/3Ssl5u5P0ZbqC2" +
            "bpmmSW+r7n3/6ZuW9TKj709cerB1mm7ttuTc9Ven6eZq3Rpduv/V+5mWQuy9Ldzb/dpbmzftfTJXWpt6rriwQ2tff3sxPX/7Ynp58XrItuRcl9dXTVuj9n+e" +
            "1vvPYUvPp/vV1rH9TH+fDPGT78htybkqWqP2/3QjtF7TaPPmSGtTzzXEQB25LTlXRWvU/p9uhNZrGm3eHGlt6rl2/5PvB6O2Jeeq2iP7fxp7tA1t3gxpbeq5" +
            "hviGCgDVDFQAaMBABYAGDNTGtEA/7a7Xaz/7Un2/qlrQzk+G3p93A7UxLdDvWnK99rMv1ferogXt/OTo/Xk3UBvTAv2uJddrP/tSfb8qWtDOT47en3cDtTEt" +
            "0NuWXq/97Ev1/WrdgnZ+svT+vO++5fux1m3VtduSvbdD0/az+rylSWu9pp2HubSLt219pzFQj/39d9+c1Op89ODhKm3JU9ezlt7300DdVtp5mKu3gfrBXfez" +
            "6v2Ten/n8pPvEWltyd7boWn7ybach/WltYv3dH8N1CPS2pK9t0PT9pNtOQ/rS2sX7+n+DtPyvau0tmRiv3KOtP1kW87DNtLaxXu5v76hAkADBioANGCgAkAD" +
            "cQNVq/Owqv0B5tMubiNtPXcVN1C1Og+r2B/gbrSLl0tbzxJxA1Wr87CK/QHuRrt4ubT1LBE3ULU6D2u9P8DdaRfvaz1LxaUHq1W3cNPatmkpx+pU21xp6biU" +
            "NN0o0lrcaetZKi11WW24sMPl9VVUC/fYer7+9mJ6/vbF9PLi9S5al5Bk7ecr7XlPex/2Lu4n32ppLVwtU9hWWos7bT2cbriBmtbC1TKFbaW1uNPWw+mG+8k3" +
            "rRepZQrbS2txp62H0wz3DRUAKhioANCAgQoADRioG+u1BboX1ftfzf1tY6372/t5O1Xv678rA3VjPbZA96R6/6u5v8uteX97P2+n6H39SxioG+uxBbon1ftf" +
            "zf1dbs372/t5O0Xv61/CQN1Yby3Qvane/2ru7zJr39/ez9sxva9/qbiW72ht27l635+50lqg1e3TY9ZuvaY9L2kt6LQ271z2s624sENa6zKN/dnW1u1T9zeL" +
            "57Gt3vcz7ifftNZlGvuzrYT2qfubw/PYVu/7GTdQ01qXaezPthLap+5vDs9jW73vZ9xPvmmtyzT2Z1spe+r+ZvA8ttX7fsZ9QwWAHhmoANCAgQoADRiojeyl" +
            "BZrShk1tgdqfbY3SwqVPBmoDe2qBJrRhk1ug9mdbI7Rw6ZeB2sCeWqAJbdjkFqj92dYILVz6ZaAutLcW6NZt2PQWqP3Z1t5buPQtruVb3Zas/vzWrdf0dmVv" +
            "+znX2m3krVupaZbuf2/t6/RWc9rzG9f63noBe9O69ZrerqymndvW1vs519L9760Nm7aepXo7b0v5ybexitZrcruymnZuWwn7OdeS/e+xDZu2niV6PG9LGKiN" +
            "VbRek9uV1bRz20rYz7mW7H+Pbdi09SzR43lbwk++jVX1JVPbldVSrnkv+9/rNdx1/3ttw6at5672cA1z+IYKAA0YqADQgIEKAA3EDVStTm7Szm0rZT/X0mv7" +
            "ei/nrUrq/sQNVK1ObtLObSthP9fUY/t6T+etQvL+xA1UrU5u0s5tK2E/19Rj+3pP561C8v7EDVStTm7Szm1r6/1cW2/t672dt9bS9yeu5TtXWiuydduz2t5a" +
            "tb2v55j0tvNcaferd72/D3u/v92HHUZrRba2t3bo3rlfHOJ9uK24n3znGq0VWWFP7dARuF98H+/DbXU/UEdrRVbYUzt0BO4X38f7cFvd/+Q7Wiuyyl7aoaNw" +
            "v/gUZ2Jb3X9DBYAEBioANGCgAkAD3Q/U0dqkME25LVO25X24re4H6mhtUkhumbIt78NtdT9QR2uTQnLLlG15H26r+4E6WpuUsaW3TNmW9+G24lq+x1qU6e3Z" +
            "6pZv2npaS7+/aa3UatXXO1f1/lS/f1rv59rPy9bSW9ZxYYdjLUot031Lv7+jtVJd721Lz2fr/Ux/XlpLv964n3xPaVFqme5b8v0drZXqer9ryfms2M/k56VC" +
            "8vXGDdRTWpRapvuWfH9Ha6W63u9acj4r9jP5eamQfL1xP/me2qLUMt231PubuKZKrvfT7no+q/Yz9Xmpknq9cd9QAaBHBioANGCgAkADcQP11BblXVum1Z9P" +
            "G6n7P1orNeV61zoPKe+ftaSt51Sp74e4gXpKi3JJy7T681kuef9Ha6UmXO+a5yHh/bOmtPWcIvn9EDdQT2lRLmmZVn8+yyXv/2it1ITrXfM8JLx/1pS2nlMk" +
            "vx/iBuqxFuXSlmn157NM+v6P1krd+nrXPg9bv3/WlraeY9LfD3Et37mq25tz9dbG/FhaS3au1vuf3hbmsK3bvGmt3fT3Ye/nv/uBevXum5PamI8ePFyl/Wig" +
            "bqtq/+96fkZ7oaSpfj9Uf37aeZ5rtPMf95PvXNq/VHN++pXQ5k07P2nr2ZPuB6r2L9Wcn34ltHnTzk/aevYkruU7l/Yva3B++pTS5k07P2nr2Yvuv6ECQAID" +
            "FQAaMFABoAEDlV2papNqR/MpvbZ/nbcaBiq7UtEm1Y7m+/TY/nXe6hio7EpFm1Q7mu/TY/vXeatjoLIrrduk2tEc0lv713mrFZcePNbGXKo6bVWdHpy7/q1T" +
            "iOmt1Ll6b8NWP1/HWH/WeZ6r9/2vFjdQT21j3vnzDdRNpLZS5+q9DVv9fJ3K+jPO81y973+1uJ98T2lj0pc9tVJ7b8MmPF/Wn3Oe5+p9/6vFDdRT2pj0ZU+t" +
            "1N7bsAnPl/XnnOe5et//anEtX33JfdpLK7X3NmzK82X9NZ9fLWVNqfsT9w0VAHpkoAJAAwYqADRgoO5cVQt0LVq4+5TWtl2L89xG6v4YqDtX0QJdkxbu/qS1" +
            "bdfkPC+XvD8G6s5VtEDXpIW7P2lt2zU5z8sl74+BunOtW6Br08Ldl7S27dqc52XS9ycuPZiSyuvV0tbl1vuf3krden8+Vp2irE51prVt0/Znrt7Pf9p+zuUb" +
            "6s58/e3F9Pzti+nJ+dPp8vpq6+XMtnT9l9dX05Pzp9Pzty8+Ge7ufX/2xv1qy35uy0DdoeTW5SlGbqWOxv1qy35uy0DdoeTW5SlGbqWOxv1qy35uK67lSxup" +
            "rctTjdpKHY371Zb93JZvqADQgIEKAA0YqADQQNxA7b3VmSK1dVltlFZqdQu39/2BLcQN1N5bnQmSW5fVRmilVrdwe98f2ErcQO291ZkguXVZbYRWanULt/f9" +
            "ga3EDdTeW51bS29dVtt7K7W6hdv7/sCW4lq+c2lLtlXdMk1rpR5rny7V+/nprQ279nrm3t+0/ax+fltbu+08l7ADQ7u8vpq+OH82vbp4s/VSIh3bnw9t2JcX" +
            "r6cvHz+b7t/7fOUVZq/nY73tZ5r0/Yn7yRfWdEr7dGQ9tmHT1nNTj/uZJnl/DFSGdkr7dGQ9tmHT1nNTj/uZJnl//OTL0PRMD+u1DZu2ng963c80qfvjGyoA" +
            "NGCgAkADBioANGCgckt161VLlpvS2t3O/2Ep9yt1fwxUbqluvWrJclNau9v5PyzhfiXvj4HKLdWtVy1Zbkprdzv/hyXcr+T9MVC5pbr1qiXLTWntbuf/sK3v" +
            "V/r+aPkekdZirW6B9q66zTtXb+enWnqrdq7q59H96ouwQ2e0QA/T5j1s6/3Z2/msfh7dr774ybczWqCHafMelrA/ezqf1c+j+9UXA7UzWqCHafMelrA/ezqf" +
            "1c+j+9UXP/l2Rgv0sBGveY6U/dnL+ax+HlP2aC/3q5pvqADQgIEKAA0YqADQgIHamd5boNVSWqOpet+ftPOf0v4lg4Hamd5boNUSWqPJet+ftPOf0P4lh4Ha" +
            "md5boNUSWqPJet+ftPOf0P4lh4Hamd5boNW2bo2m631/0s7/1u1fssS1fLduV1Zbu7W7dWs0vS1c3YLe2tr733o/01q46ed5rurWbtp5qBY3UK/efTNEi/XR" +
            "g4ertDFP3c+7rqf686vtfaB+sNb+V+1n9flcaz1pehuoH6Tuf9xPvgntymprtjETWqNaoNvqff/TWri972fvkvc/bqAmtCurrdnGTGiNaoFuq/f9T2vh9r6f" +
            "vUve/7iW7yi9yLXamCmtUS3QbfW+/2kt3N73s3ep+x/3DRUAemSgAkADBioANGCg7lxa+zSNVmpbVfuphVuj1/dD2no+MFB3Lq19mkYrta2K/dTCrdPj+yFt" +
            "PTcZqDuX1j5No5XaVsV+auHW6fH9kLaemwzUnUtrn6bRSm2r9X5q4dbq7f2Qtp6PxaUHq1NYc1Wn6eauP62dW32/tFgPS3teejdae3au6tZ67+czLuzAYZfX" +
            "VwfbpF9/ezE9f/tiennxOrJ1Odex651rb/tDtr2dt9bP4974ybczo7VztVjp3Z7O2wit9SUM1M6M1s7VYqV3ezpvI7TWl/CTb2dGa+dqsbIHezlve7iGSr6h" +
            "AkADBioANGCgAkADBuoRaS3QtPZm9XrS2rBp0s4Dn2b/x2CgHpHWAk1rb1avJ60NmybtPPBd9n8cBuoRaS3QtPZm9XrS2rBp0s4D32X/x2GgHpHWAk1rb1av" +
            "J60NmybtPHCb/R+Llm9jaetPa/+mrSdN2v5Ut1t713ubOu3+pr3P5zJQG0tb/9W7b05qbz568HCV1mjaetKk7c+p6xnV3Oe3aj/veh7S7m/a+3wuP/nuXFr7" +
            "N209adL2R7u1rbQ2tfvbloG6c2nt37T1pEnbH+3WttLa1O5vW1q+O5fW/k1bT5q0/RnxHlRKa1O7v235hgoADRioANCAgQoADRioO6f12hf3izWltcp7Z6Du" +
            "nNZrX9wv1pTWKu+dgbpzWq99cb9YU1qrvHcG6s5pvfbF/WJNaa3y3g2XHqxupfa+/tHanmnt3LnSznOaueen9+s9Jv089J4eHC7scHl9dbBd+fW3F9Pzty+m" +
            "lxevI1uy1es/9vl74zwwEueh1nA/+aa1UueqXv9obU/ngdE4D3WGG6hprdS5qtc/WtvTeWA0zkOd4X7yTWulzlW9/sRrruQ8MCLnocZw31ABoIKBCgANGKgA" +
            "0EDcQK1umfr8PvS+/r3QeuUm5+GwuIFa3TL1+fl6X/+eaL1yk/NwWNxArW6Z+vx8va9/T7Reucl5OCxuoFa3TH1+tt7Xvzdar9zkPBwW1/JlW9Ut4mppLeLe" +
            "pLes01q7aec/7fltfb/S29px31Bhicvrq+nJ+dPp+dsXhukdfGi9Pjl/Ol1eX229HLgl/XwaqOzKaC3iClqvJEs+nwYquzJai7iC1ivJks/ncC1f9k2ftA2t" +
            "V5Klnk/fUAGgAQMVABowUAGgAQOVW3pvBWuNtuH+0kLV/Uo9nwYqt/TeCtYaXc79pZWK+5V8Pg1Ubum9Faw1upz7SysV9yv5fBqo3NJ7K1hrdBn3l5Za36/0" +
            "8xnX8k1rdfauutV5rJ2b3t5Ma8+mff5cae3W6vfJ3P2sfl7SzsNofENlkWPt3PT2Jtn2dn48L/tmoLLIKe3c5PYm+fZ0fjwv+2agssgp7dzk9ib59nR+PC/7" +
            "puXLIqf2NFPbm/RhL+fH87JvvqECQAMGKgA0YKACQAMGKouM0v5NXT+HpbV/nbd9M1BZZIT2b/L6OSyt/eu87ZuByiIjtH+T189hae1f523fDFQW2Xv7N339" +
            "HJbW/nXe9q37lu9oLcrq/em9zZsmreW7d6O3cHt/P/T+/hF24JbL66vpi/Nn06uLN5/88w+t0ZcXr6cvHz+b7t/7fOUVwvdzPmtVvx96f//4yZdbtEbpnfNZ" +
            "p/r90Pv7x0DlFq1Reud81ql+P/T+/vGTL7dojbIHzmeN6vdD7+8f31ABoAEDFQAaMFABoAEDFQpVt1vTWrUptHBrpJzn1PtroEKh6nZrWqs2gRZunYTznHx/" +
            "DVQoVN1uTWvVJtDCrZNwnpPvr4EKharbrWmt2q1p4dba+jyn39/hWr5p7dPq9W+9P9Vtz7XXU633lmnv0t4P1dJaxL3zDZVSH9qbT86fTpfXV7P/95fXV9OT" +
            "86fT87cvFg/TFuupdux609cPIzNQKVfd9lxzPdV6b5nCyAxUylW3PddcT7XeW6YwMi1fVlHd9pwrtQXae8sURuYbKgA0YKACQAMGKgA0YKASrapVm9oC7b1l" +
            "CiMzUIlW0apNboH23jKFkRmoRKto1Sa3QHtvmcLIDFSitW7VprdAe2+Zwsi0fDe295bvXGu3alu3gufaW+u4ukVc/fnVz9dcvT+PaeezmoG6MQP10x49eDh9" +
            "+fjZdP/e56X/P1fvvpm+OH82vbp4U/r/c8xdr7dq/dXrSf18A/XT9nI+q/nJlzhrtmorWsFz7al1XN0i1jpe357OZzUDlThrtmorWsFz7al1XN0i1jpe357O" +
            "ZzUtXyKt1apN6eHupXVc3SLWOt7GXs5nNd9QAaABAxUAGjBQAaABA5VIa7Vqq1rBa0lrHVe3iLWO+5J2PqsZqMRZs1Vb0QpeU1rruLpFrHXcl7TzWc1AJc6a" +
            "rdqKVvCa0lrH1S1ireO+pJ3PagYqUdZu1bZuBa8trXVc3SLWOu5L2vmsJj24sdHSg9WpturW61yt9zO9ZTrX3u/X2tLeJ6PxDZVduby+mp6cP52ev33xyRD3" +
            "199eTM/fvpienD+dLq+vNljhMr2v/2N7v1+MxUBlV0Zovfa+/ptGuF+Mw0BlV0Zovfa+/ptGuF+MQ8uXXRml9dr7+j8Y5X4xBt9QAaABAxUAGjBQAaABA5Vb" +
            "em9vjtJ67X39tOU8ZDBQuaX39uYIrdfe109bzkMOA5Vbem9vjtB67X39tOU85DBQuaX39ubeW6+9r5+2nIcsw7V8e9f7/qS1W6vX0/v9muvYfi5VvT+936/q" +
            "/T9mb63puYQdWNXl9dX0xfmz6dXFm0/++Yd268uL19OXj59N9+99PtR6endsP6m19f6P/rz4yZdVpbVb09bTu1P2kzoJ+z/y82Kgsqq0dmvaenp3yn5SJ2H/" +
            "R35e/OTLqtLarWnr6Z092lbK/o/6vPiGCgANGKgA0ICBCgANGKisapTWLm05D30Z9X4ZqKxqhNYubTkPfRn5fhmorGqE1i5tOQ99Gfl++WczrOpDazclPUg2" +
            "56Evo9+v7lu+HDa3Ndq6Bbr0Adu6TTqa9HZx2nlovf6193+utOtN4xsqt7RugS5te27dJh1Neou19/MwWjt6tOv131C5paIFuqTtmdAmHU1yi7X38zBaO3q0" +
            "6zVQuaWiBbqk7ZnQJh1Ncou19/MwWjt6tOv1ky+3VPU379r2HLEHmiC1xZq4pjlGa0ePdr2+oQJAAwYqADRgoAJAAwYqt5za2p3rrm3PqvVwWPX96v08jLY/" +
            "qdebxkDlllNau3MtaXtWrIfDqu9X7+dhtP1Jvt40Biq3nNLanWtJ27NiPRxWfb96Pw+j7U/y9aaRHty5rdNu0oN9qb5fvZ+H0fYn/XrTxA1UAOiRn3wBoAED" +
            "FQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAED" +
            "FQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoAEDFQAaMFABoIH/BRO8PT54CAqaAAAAAElFTkSuQmCC";

        private const string AlipayQrBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAdQAAAHUCAYAAACDJ9lsAAAgUElEQVR4nO3cv44V17LH8bX5M+PxxVi6CdkVz2DxAJaIiC05QgSczAHp5RVOSkAGAXJk6TwC" +
            "EjEanWcYnYzkSoO52sdjNvsEaOTZMNPda3fV6l+t+n7S69uqVV3d5d3H81t9WG+2BQAAzHJt6QIAAOgBCxUAAAMsVAAADLBQAQAwwEIFAMAACxUAAAMsVAAA" +
            "DLBQAQAwwEIFAMAACxUAAAMsVAAADLBQAQAwwEIFAMAACxUAAAMsVAAADLBQAQAwwEIFAMAACxUAAAMsVAAADLBQAQAwwEIFAMAACxUAAAMsVAAADLBQAQAw" +
            "wEIFAMAACxUAAAMsVAAADLBQAQAwwEIFAMAACxUAAAMsVAAADLBQAQAwwEIFAMAACxUAAAMsVAAADNxYuoAv/c///r50CV3519+/c73+9Wul/P7vbXn2+qz8" +
            "459/lnfvt7Oud+f2qvz0w83y5P5B+e6bVdl8sq1H/freuF/L3i/eb7a832+1Vh/Wm3lPlDEGzpb3wH34Y1sevVyX45ON6XXv3b1eXj0+KrcOVy71qF7fG/fL" +
            "9vq1eL/ZUluofPLFLM9en5Xjk025YThJN66VcnyyKc9en7nUo3x9b9wv2+sDF7FQMctvx3+W1aqUj4afyj5+KmW1+nxtj3qUr++N+2V7feAiuf8NFbH83//7" +
            "/C8G2+1+1576/6N6fW/cL9vrAxfxCxUAAAMsVAAADLBQAQAwwEKFpNWqlP/+L60/OSnlc02rCWWp1u9F9bzcL7TEQoWcG9c+/0ciP9+7uXQpX/n53s2y3ZbR" +
            "P8NQrd+D8nm5X2iJhQo5Hz99/kP7J/cPli7lK0/uH5R7d6+P/hmGav0elM/L/UJLLFRIuXN7VX758aD8+rej8v2R3ie4749W5de/HZVffjwod25/XZ96/dbU" +
            "z8v9Qkvh/w5VLXrKW/Tospr7tflknz071+ZTKd8erMrTB4fl6YPD0X9WLTu3ltr9Gpv/y/pjeb+U+9+D6O+38AsVfTtd+2TPtmJd/7v32/L8zVl5e7KRzApe" +
            "+n7N7c9Y/er9x7L45AtpHtmzLall53pTuF9k/2IpQV9TyMIje7Yltexcbwr3i+xfLIVPvpAWPV9VLTvXm0pNZP9iCfxCBQDAAAsVAAADLFQAAAywUJFS9OzW" +
            "fevPkm1Lf7AEFirSiZ7dOqf+DNm29AdLYaEinejZrXPqz5BtS3+wFBYqUome3Tq3/t6zbekPlpTu71DVsiLVsjqXzmKdqzZ7tpZ3f7zrr822VUN/hvF+W1a6" +
            "hYphS2exqqM/AK7CJ1/sUMhiVUZ/AFyF1wJ2KGSxKqM/AK7CJ1/sIJ90GP0BcBV+oQIAYICFCgCAARYqAAAGWKjoQqts1alZr7V6yYb16g8QAQsV4bXMVp2S" +
            "9Vqrp2xYj/4AUTD2CK9ltuqUrNdaPWXDevQHiIKFitBaZ6uOZb3W6i0b1ro/QCT8HSpmqc3qrM0a9c5urVWT9brPtb1ZZxHfub0qP/1wszy5f1C++2ZV3R/v" +
            "7Nmx815WP7AvFiqQiHUW8bv32/L8zVl5e7Iprx4flVuHWr9Kx86rXj9i4ZMvkIhHFvGNa6Ucn2zKs9dndhc1MuW8yvUjFhYqkIhHFvHHT5//7Oe34z/tLmpk" +
            "ynmV60csfPIFEvHKIt5uNXOOp9akWj9i4RcqAAAGWKgAABhgoQIAYICFiqamZr32km2rRi2LmHlAT1ioaGpK1mtP2bZq1LKImQf0hIWKpqZkvfaUbatGLYuY" +
            "eUBPWKhoaizrtbdsWzVqWcTMA3rC36GiqZqs182nNtmzNazr8TY3a7fGlPs1p54WObtj2cJk/2IICxXS1LJnrevx1jqrtvfs3Oj1wxeffCFNLXvWox5vLbNq" +
            "M2TnRq8ffgK9FpCRWvasRz3eWmbVZsjOjV4//PDJF9LUsmej5r22yqrNkp0bvX744BcqAAAGWKgAABhgoQIAYICFipS8s2ezUsvm5X6hJRYq0vHOns1MLZuX" +
            "+4WWGDOk4509m5laNi/3Cy2xUJGKd/ZsdmrZvNwvtJTu71D/9ffvli4hNe8s3Jr7650928JY9uxc3tm8+1y/Ru398u6nN95vy0q3ULEstSzc3rNn5/LuD/1H" +
            "T/jki6bUsnAzZM/O4d0f+o+eiLzWkIVaFm6G7Nk5vPtD/9ETPvmiKbX80yzZs/vy7g/9R0/4hQoAgAEWKgAABlioAAAYYKGiC97ZvK2yZ2t51x/9+kBLLFSE" +
            "553N2zJ7tpZ3/dGvD7TEQkV43tm8LbNna3nXH/36QEssVITmnc3bOnu2lnf90a8PtLT6sN5I/XFX9CxNNbXZnrX9V7u+dzYs1+/7+mrzj2Fq2cUEO6Ar0bNn" +
            "uf6y1wfm4JMvuhI9e5brL3t9YA4WKroSPXuW6y97fWAOPvmiK9GzZ7n+stcH5uAXKgAABlioAAAYYKECAGCAhYqukA07jP4Aflio6ArZsMPoD+CHhYqukA07" +
            "jP4Aflio6ArZsMPoD+BHLssXy1LLGlXL6lTLevXuz1h2bnZq84llEewA4Epj2bkA/sInXwBXmpKdC+AzHhMAV5qSnQvgMz75ArgSebjAdPxCBQDAAAsVAAAD" +
            "LFQAAAywULFjatYrAGAXCxU7pmS9AgC+xmsTO6ZkvQIAvsZCxY6xrFcAwOXk/g51LDv0zu1V+emHm+XJ/YPy3Tersqn8JdV7Nunc/mw+lfLtwao8fXBYnj44" +
            "NK9PLSs4+jyM9XPuPNTyzrZVy1KuFX3exrSeNzVyC3UsO/Td+215/uasvD3ZlFePj8qtw7pfUb1nk87tTzbMA1pi3vom98l3SnbojWulHJ9syrPXZy7Xj25O" +
            "f7JhHtAS89Y3uds6JTv046dSVqvP/6zH9aOb059smAe0xLz1Te6T79Ts0O12v5zRLNmk+/Ynmyw9Yh40ZLkHWedN7hcqAAARsVABADDAQgUAwEC6hZolq3a1" +
            "+nxWxJJlPlVN7T/PFy6TbqFmyKq9ce3zfxTw872bS5eCShnmU9mU/vN84SrpHtsMWbUfP5Vy7+718uT+wdKloFKG+VQ2pf88X7hKuoXae1btndur8suPB+XX" +
            "vx2V74/6O1/vep9PdWP95/nCkNWH9Ubqj4WsszSzZ0tGz0auzWJVy0pVy7Zd2tx5i/5+8L5fatnF1s+v+vtcLtjBWvZsyWzZyGr1YJfa86hWT3be7ytvKT75" +
            "Zs6WzJaNrFYPvqb2PKrVk5n3+8pbitdO5mzJbNnIavXga2rPo1o9mXm/r7x1/8n3XNZsyWzZyGr14HJqz6NaPVl5v6+8pfiFCgCANxYqAAAGWKgAABhIs1Cz" +
            "Zm96Z5N6Zc+q1YO+RX8/9PK8RM9STrFQM2dvemeTemTPqtWDvkV/P/T0vETPUhZpo6/M2Zve2aQe2bNq9aBv0d8PPT0v0bOUu1+o2bM3vbNJrbNn1epB36K/" +
            "H3p7XqJnKYfP8o2W9eqdnat+fTVLZ+G2zrZVyxb2zp71Pm+0LOvsWcre0gQ7nFs669U7O1f9+thFP2Nb+n3ypWjPr1o9c3X/yfdLClmv3tm5ytfH1+hnXArv" +
            "k4siPr9q9cwhMgbtKGS9emfnKl8fX6OfcSm8Ty6K+Pyq1TNHuk++KvmP3tm5qtfH5ehnTGr3LOrzq1bPvtL9QgUAwAMLFQAAAyxUAAAMsFAX4p29qZp1Ccyh" +
            "Nv9qWbhR9fK+YqEuwDt7UznrEphDbf7VsnAj6ul9xRgswDt7UznrEphDbf7VsnAj6ul9xUJtzDt7Uz3rEphDbf7VsnCj6e19lS7LVy0L1Fvv2cWtqc3b0lnE" +
            "c6k9X2pZuNHnQe3+eksX7JDN0lmj3tnCgKVoWbjQwiffzilkjXpnCwNWImbhQgevqc4pZI16ZwsDViJm4UIHn3w7p5KP6Z0tDFiImoULDfxCBQDAAAsVAAAD" +
            "LFQAAAzILVTvrE61LFAMU8lKVZ03lf5gmizvn+j170tuoXpndaplgWKYQlaq8rwp9AfTZXj/RK9/DrnH0DurUy0LFMMUslKV502hP5guw/snev1zyC1U76xO" +
            "tSxQDFs6K1V93pbuD+r0/v6JXv9cclm+tbyzN5e+fm+8sz29s1KXnoe516+llg07pnXWrnc9au8HtedFTfiF+uGPaVmv9+5e3yt7U+X6vYi+UFXmYd/r14q2" +
            "UM/t2x+v8/byflB7XtTIffKt5Z29qXB96FCYB7Jkh6n1J/P7Ids8B71Nf/HO3lS4PnQozANZssPU+pP5/ZBtnsNn+Xpnb6pcHxpU5oEs2WFq/cn6fsg2z+F/" +
            "oQIAoICFCgCAARYqAAAGwi/U6NmYWbJYVftfSyWbt1U/o86nWjayWj3wEX6hRs/GzJDFqtz/WgrZvC37GXE+1bKR1eqBn/C3KXo2ZoYsVuX+11LI5m3Zz4jz" +
            "qZaNrFYP/IRfqNGzMXvPYlXvf62ls3lb9zPafKplI6vVA19y0YNq2Y9q2Z61vKP4vHlnmXqfV22e1eqJnr1cS60e7/5Hf3/WkluoatmPatmetaIvVO8sU+/z" +
            "qs2zWj3Rs5drqdUTbaGeU83+lfvkmyH7MXr9LZFl2nc93tTOq1ZPRMr9kXtNZch+jF5/S2SZ9l2PN7XzqtUTkXJ/5LJ8s2Q/Rq+/leg9UptntXq8qZ1XrZ6o" +
            "VPsj9wsVAICIWKgAABhgoQIAYEBuoWbJMu0l29Yb/bel9nx5y3beWlH7o1bPObmFmiHLtKdsW2/035ba8+Ut23lrReyPWj0XyS3UDFmmPWXbeqP/ttSeL2/Z" +
            "zlsrYn/U6rlIbqH2nmXaW7atN/pvS+358pbtvLWi9Uetni/JRQ/Wss56Vc+KJNs2Vj1e0WtTqfVzrqXnszW1KNDo99db+IXqlfWqmhVJtm2sepZeqOfU+rkv" +
            "lflsRW3BRL+/3uQ++dbyyHpVzook27bvejyo9VNZ9Pq90Z9h4dvikfWqnBVJtm3f9XhQ66ey6PV7oz/D5LJ8a3nlOapmRSrWVEMty1StHi9q/VQVvX5v9GdY" +
            "+F+oAAAoYKECAGCAhQoAgAEWajBk22If0bOvW4lef1S9vH9YqMGQbYta0bOvW4pef0Q9vX8Ym2DItkWt6NnXLUWvP6Ke3j8s1GDItkWN6NnXrUWvP5re3j/h" +
            "owe9o91qo7DUsmrV1N4v6/7PVVuP93lrqc2nWn9qLR0tOfd+qT0v0YUPdlBzuh7Ounz3fluevzkrb082klnB0Y31Pzvmsy9z7xfPiy0++RrLkA2rjKzRYcxn" +
            "f+bcL54XW7TRWIZsWGVkjQ5jPvsz537xvNjik6+xLNmwqujpMOazT/veL+6xLX6hAgBggIUKAIABFioAAAZYqFfYN1tyahZoL9mVashiHaY2n2r1ZEM2uC0W" +
            "6iXmZEtOyQLtKbtSDVmsw9TmU62ebMgGt8Vr5xJzsiWnZIH2lF2phizWYWrzqVZPNmSD22KhfmFutuRYFmhv2ZVqyGIdpjafavVkQza4rXRZvtGzedXqqaWW" +
            "3bp0Fqs39Xmo5T3/atm21vOpPg/R328s1BEf/piWdXnv7vUm2adq9dRioS5DdR5qec//1Ovva+mFek51HqK/3/jkO0It+1StHujraR685z9Dtq3yPER/v3U8" +
            "NjbUsk/V6oG+nubBe/4zZNsqz0P09xtZviPUsk/V6kEMvcyD9/z30KMpVOch+vuNX6gAABhgoQIAYICFCgCAgfAL1TsLVC1rVK2eWtHrj6qXfqo879H1Mg9q" +
            "wi9U7yxQtaxRtXpqRa8/op76qfC8R9fTPKgJPzbeWaBqWaNq9dSKXn9EPfVT4XmPrqd5UBN+oXpngapljarVUyt6/dH01s+ln/foepsHNeGjB6OLnl2pxjuL" +
            "tZZ3dGIttf6M8c7mnXt9tehK66zyWq37qfZ8sVAXFj27Uo13FmsttQderT9TeWfz7nv96AvVax5a9VPt+Qr/yTe66NmVajJksc4RsT/e2byZny+Pecjcz0CP" +
            "VZ+iZ1eqyZDFOkfE/nhn82Z+vjzmIXM/yfJdWPTsSjX0aFjU/nhn82Z9vrzOnLWf/EIFAMAACxUAAAMsVAAADLBQAXSL7N9havVHz/pmoQLoFtm/w9Tqj571" +
            "LdJGALBH9u8wtfqjZ32zUAF0i+zfYWr1R8/6losejJY1CrSknpXqHcXnHTWnlq1t3U+17OLessrlfqGerrfl4Yt1ef7mjGUKfOHd+215/uasPHyxLqdrng9r" +
            "Y++f6P1Xq1+tnrnkFmrErFGgpcxZqd4yZP+q1a9Wzxxyayti1ijQUuasVG8Zsn/V6lerZw65LN+M+Y9AraxZqd6yZP+q1a9Wz77kfqECABARCxUAAAMsVAAA" +
            "DLBQgUTIth0WPUsWy2KhAomQbTssepYslhV07AHsg2zbYdGzZLEsFiqQCNm2w6JnyWJZclm+0bNAvbNSa1HPMLV5U+uPd7atd//HzK2f7PG21LN/+YUK4Epk" +
            "2w4je7wt9XljoQK4Etm2w8geb0953hgDAFci23YY2ePtKc+bXJYvAB1k2w6LfObIVOeNX6gAABhgoQIAYICFCgCAAbmFShZo3+jPMLX+qNUDlKI7b3ILlSzQ" +
            "vtGfYWr9UasHUJ43ubVFFmjf6M8wtf6o1QMoz5vcQiULtG/0Z5haf9TqQW7q8yaX5VtLLfvUm3d2aG1/vLNeyZK15d3PWt7Pb/Tr1yKbelkEOwRzut6WRy/X" +
            "5fhks3QppZTxes6zN9+ebMqrx0fl1mHdv1V6X39prevvvZ/AkuQ++WKYWnaod9YrWbK2MvQTWIrIaxlTqWWHeme9kiVrK0M/gaXwyTcYtfxK76xXsmRtZekn" +
            "sAR+oQIAYICFCgCAARYqAAAGwi/UbFmjXlnHvfSnVvTs6FrZnhdvav1Uez+o9cdb+IWaLWvUI+u4p/7Uip4dXSvb8+JNrZ9q7we1/ngL/xrJljXqkXXcU39q" +
            "Rc+OrpXtefGm1k+194Naf7yFX6jZskats45760+t6NnRtbI9L97U+qn2flDrjze5LF+1rFHv7Nwxrc9ba+ks3C9Fz0q1lu158aaeTe0tena3N7mF+uGPaVm1" +
            "9+5eb5I1OrUeb63OW0ttwbBQL5ftefG2bz/V3m+1vOuP3h+5T75qWaMK2blkq2KObM+Lt8zZ1GR3D5Mbe7WsUYXsXLJVMUe258Vb5mxqsruHyWX5qmWNquSZ" +
            "kq2KObI9L96yZlOT3T1M7hcqAAARsVABADDAQgUAwAALdUS2rNda2fqT7by16M+w6Nm23vVH7w8LdUS2rNda2fqT7by16M+w6Nm23vVH7w9jPyJb1mutbP3J" +
            "dt5a9GdY9Gxb7/qj94eFOiJb1mutbP3Jdt5a9GdY9Gxb7/qj90cuerA22q02as47K3LpaDr1rEvv/qjNg7fo8+b9vNdSq6eW2jyr1eMt3S/U0/W2PHyxLs/f" +
            "nF0a3P3u/bY8f3NWHr5Yl9O11L9rTBK9/tZ6nwdv9EeL2jyr1eMt3UKNnhU5RfT6W8owD97ojw61eVarx1u6hRo9K3KK6PW3lGEevNEfHWrzrFaPN7ksX2/R" +
            "syKnil5/K1nmwRv90aA2z2r1eEv3CxUAAA8sVAAADLBQAQAwwEI1ppJlqpp16dUf1fN6U5k3aIiehRsdC9WYQpapctalR3+Uz+tNYd6gI3oWbnQ8hsYUskyV" +
            "sy49+qN8Xm8K8wYd0bNwo2OhGls6y1Q969K6P+rn9bb0vEFL9Czc6NJl+apldS6dXVxLPXvT+rxf8p4HtezTpbOCa6llcc+tx3sevJ+XWmrZyLXSBTtkc7re" +
            "lkcv1+X4ZGNyvfPszbcnm/Lq8VG5daj1b7nW521trH71/i9NrT9z6/Geh+jPixo++XZuSpZmLeXsTY/ztpQt+9SDWn/m1OM9D9GfFzW0sXNTsjRrKWdvepy3" +
            "pWzZpx7U+jOnHu95iP68qOGTb+e88jFVszcVa6qRLfvUi1p/9q3Hex6UetQDfqECAGCAhQoAgAEWKgAABtIt1GxZl9myc7OdF5iDLGhb6RZqtqzLbNm52c4L" +
            "zEEWtK10bcyWdZktOzfbeYE5yIK2lW6hZsu6zJadm+28wBxkQdtKl+VbyztLUy071Fv0bOFs86D2PEbLFlYTPQta/f2W7hdqrdP1tjx8sS7P35xd+vI/z9J8" +
            "+GJdTtfL/7uJWj1fGutnrdbnZR4QWfR5UK+fhToiYraqWj0XRc8WZh4QXfR5UK6fhToiYraqWj0XRc8WZh4QXfR5UK6fLN8RUbNV1eo5Fz1bmHlAD6LPg2r9" +
            "/EIFAMAACxUAAAMsVAAADMgtVLWsXe96yJ610ct5yVYF4pJbqGpZu971kD07X0/nJVsViEvusVXL2vWuh+zZ+Xo6L9mqQFxyC1Uta9e7HrJn5+ntvGSrAnHJ" +
            "ZfliWWpZtd7ZsLXU6veuRy2LVe282ebTOot7LrX+E+yAHafrbXn0cl2OTzaX/t/PszTfnmzKq8dH5dYhv6IwHfMT29j7ITu5T75YVsSsWsTC/MTlkcXdE9qC" +
            "HRGzahEL8xOXRxZ3T/jkix1Rs2oRC/MTE/dsGL9QAQAwwEIFAMAACxUAAAMsVOxQy1LGssiahiLV+WGhYodaljKWRdY01CjPDwsVO9SylLEssqahRnl+WKjY" +
            "oZaljGWRNQ0l6vMj93eo1tmh2dVmXW4+lfLtwao8fXBYnj44HP1n1bI91USf55p52Ofa3tT6752VHV3N+0qxN3ILFbGQ7QlMR1Z23/jki1nI9gSmIyu7b7wG" +
            "MQvZnsB0ZGX3jU++mIVsT2A6srL7xi9UAAAMsFABADDAQgUAwAALFajglW2LaaL3n6zsy/VyXhYqUMEj2xbTRe8/Wdlf6+m8QccSWIZHti2mi95/srK/1tN5" +
            "WahABetsW9SJ3n+ysnf1dt7Vh/VG6o+darM3a7Nqo/PuT7RsXvXsU+/7pZZVOyb7/fKmNg/W7x/1+eEXKnacrrfl4Yt1ef7mTH6ZlvJX9unDF+tyutavNzvu" +
            "F4aMvX/U54eFih0Rs3nJPo2F+4WrRM86DvTaRAsRs3nJPo2F+4WrRM86JssXO6Lmh5J9Ggv3C5eJnnXML1QAAAywUAEAMMBCBQDAAAsVCCx6ti361Es2by0W" +
            "KhBY9Gxb9KenbN5aPIZAYNGzbdGfnrJ5a7FQgcCiZ9uiL71l89ZK93eo0bMu0bex+bwsy/Tbg1V5+uCwPH1w2Lweteu3znpVe594q3lfqeXstsAvVCAQ9SzT" +
            "pdEfLImFCgSjnGWqgP5gKSxUIBjlLFMF9AdLYaECAalmmaqgP1gCCxUAAAMsVAAADLBQAQAwwEJFF7Jlh7Y6b9Ss4H37E/W83rI9X/tioSK8bNmhLc8bMSt4" +
            "Tn8intdbtudrDsYG4WXLDm153ohZwXP6E/G83rI9X3OwUBFatuzQ1ueNlhU8tz/Rzust2/M1V7osX8RSmx16/Vopv/97W569Piv/+Oef5d37eX+L2DobVvG8" +
            "nlnBnva5V7Xnrc3y9c7u9qxnyry1fl7UsFDRldP1tjx6uS7HJxuT651nw7492ZRXj4/KrUOtf0vPdl4sa2zess8Pn3zRlWevz8rxycb0PypRzobNdl4sa8q8" +
            "ZZ4fFiq68tvxn2W1Kqb/UYlyNmy282JZU+Yt8/zwyRdd8cpvVc2GzXZeLGvqTGSdH36hAgBggIUKAIABFioAAAZYqOhKtizWbOfFsKnz4J11rHp9byxUdCVb" +
            "Fmu282LYlHnwzjpWvr43HkN0JVsWa7bzYtiUefDOOla+vjcWKrqSLYs123kxbGwevLOO1a/vbfVhvZH6YyHvbMza63vzrj96f8bMzQ6N1k/180bPUrau39vc" +
            "/pDNa4tfqAjtPDv04Yt1OV1rv/wsqJ/3dL0tD1+sy/M3ZybLqPV5rev3Nrc/Y+dVnzc1LFSEly07VPm80bOFPer3Nqc/ZPPaCjQ2wOWyZYcqnzd6trBH/d7m" +
            "9IdsXltk+aIL2bJDVc8bPVtYsadT7Nsfsnlt8QsVAAADLFQAAAywUAEAMMBCxY5s2bBRs0PV6jkXfX6i1q86D9mwULEjWzZsxOxQtXouij4/EetXnodsAo0N" +
            "WsiWDRsxO1Stnouiz0/E+pXnIRsWKnZky4aNlh2qVs+Xos9PtPrV5yGbdFm+0UXvj3dWsPd5vbNevetXmx+1rGNv0bKUa6n33xvBDkCF0/W2PHq5Lscnm6VL" +
            "wSXOs2ffnmzKq8dH5dZhrl9tS89n9v7zyReoEDHrNZvM2bMK85m5/7wWgAoRs16zyZw9qzCfmfvPJ1+gAnmmMWTNnlU5c9b+8wsVAAADLFQAAAywUAEAMMBC" +
            "RVNRs1LPRa9fjVc/s2bbqsxn1v6zUNFUxKzUi6LXr8ajn5mzbRXmM3P/eS2gqYhZqRdFr1+NRz8zZ9sqzGfm/rNQ0VS0rNQvRa9fjXU/s2fbLj2f2fsfPssX" +
            "w6yzQFtndS49D3PPu3T9c6llQdeyzrZVm4fW9UR/n3jjFyp2nK635eGLdXn+5uzSl895VufDF+tyupb6dzEX2c7bm7F5rqU2D2r1fCnb+4SFih1TskCzZXVm" +
            "O29PPLJt1eZBrZ6Lsr1PWKjYMSULNFtWZ7bz9sQj21ZtHtTquSjb+4QsX+yYmr+ZLasz23l74XXP1OZBrZ5z2d4n/EIFAMAACxUAAAMsVAAADLBQsWNqFmir" +
            "rM7o2aQq9dfqJYtVLSs4aj2q11fDQsWOKVmgLbM6o2eTKtRfq6csVrWs4Ij1KF9fTaDHHC1MyQJtmdUZPZtUof5aPWWxqmUFR6xH+fpqiB7sXPSoMOvouFpz" +
            "z7t0/bV6i4JTix6MVo/69dXILVQAACLiky8AAAZYqAAAGGChAgBggIUKAIABFioAAAZYqAAAGGChAgBggIUKAIABFioAAAZYqAAAGGChAgBggIUKAIABFioA" +
            "AAZYqAAAGGChAgBggIUKAIABFioAAAZYqAAAGGChAgBggIUKAIABFioAAAZYqAAAGGChAgBggIUKAIABFioAAAZYqAAAGGChAgBggIUKAIABFioAAAZYqAAA" +
            "GGChAgBggIUKAIABFioAAAZYqAAAGGChAgBg4D9Hv65kpNtO+wAAAABJRU5ErkJggg==";

        public static Image GetWeChatQr()
        {
            lock (lockObj)
            {
                if (cachedWeChatQr != null) return cachedWeChatQr;

                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources\\donate_wechat.png");
                if (File.Exists(localFile))
                {
                    try
                    {
                        byte[] fileBytes = File.ReadAllBytes(localFile);
                        cachedWeChatQr = Image.FromStream(new MemoryStream(fileBytes));
                        return cachedWeChatQr;
                    }
                    catch (Exception diagEx) { Diagnostics.Log("UI/Resources/DonateQrAssets.cs: 已忽略异常", diagEx); }
                }

                try
                {
                    byte[] bytes = Convert.FromBase64String(WeChatQrBase64);
                    cachedWeChatQr = Image.FromStream(new MemoryStream(bytes));
                }
                catch (Exception diagEx) { Diagnostics.Log("UI/Resources/DonateQrAssets.cs: 已忽略异常", diagEx); }

                return cachedWeChatQr;
            }
        }

        public static Image GetAlipayQr()
        {
            lock (lockObj)
            {
                if (cachedAlipayQr != null) return cachedAlipayQr;

                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources\\donate_alipay.png");
                if (File.Exists(localFile))
                {
                    try
                    {
                        byte[] fileBytes = File.ReadAllBytes(localFile);
                        cachedAlipayQr = Image.FromStream(new MemoryStream(fileBytes));
                        return cachedAlipayQr;
                    }
                    catch (Exception diagEx) { Diagnostics.Log("UI/Resources/DonateQrAssets.cs: 已忽略异常", diagEx); }
                }

                try
                {
                    byte[] bytes = Convert.FromBase64String(AlipayQrBase64);
                    cachedAlipayQr = Image.FromStream(new MemoryStream(bytes));
                }
                catch (Exception diagEx) { Diagnostics.Log("UI/Resources/DonateQrAssets.cs: 已忽略异常", diagEx); }

                return cachedAlipayQr;
            }
        }
    }
}
