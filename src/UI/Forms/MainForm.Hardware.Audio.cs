// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 纯音提示音合成（44.1kHz PCM 双音阶，无外部音频资源）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {

        private static SoundPlayer ascendingPlayer = null;
        private static SoundPlayer descendingPlayer = null;
        private static readonly object chimeSyncLock = new object();

        private static byte[] GenerateBellChimeWav(double[] freqs, double[] durations)
        {
            try
            {
                int sampleRate = 44100;
                int totalSamples = 0;
                for (int i = 0; i < durations.Length; i++) totalSamples += (int)(sampleRate * durations[i]);
                int silenceSamples = (int)(sampleRate * 0.15); // 150ms 尾部静音缓冲，彻底根除音频驱动提前截断
                totalSamples += silenceSamples;
                int dataChunkSize = totalSamples * 2;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ms))
                    {
                        bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                        bw.Write(36 + dataChunkSize);
                        bw.Write(new char[] { 'W', 'A', 'V', 'E' });
                        bw.Write(new char[] { 'f', 'm', 't', ' ' });
                        bw.Write(16);
                        bw.Write((short)1); // PCM
                        bw.Write((short)1); // Mono
                        bw.Write(sampleRate);
                        bw.Write(sampleRate * 2);
                        bw.Write((short)2);
                        bw.Write((short)16);
                        bw.Write(new char[] { 'd', 'a', 't', 'a' });
                        bw.Write(dataChunkSize);

                        for (int n = 0; n < freqs.Length; n++)
                        {
                            double f = freqs[n];
                            int nSamples = (int)(sampleRate * durations[n]);
                            double decayRate = (n == 0) ? 12.0 : 6.5;
                            for (int i = 0; i < nSamples; i++)
                            {
                                double t = (double)i / sampleRate;
                                double env = (t < 0.005) ? (t / 0.005) : Math.Exp(-(t - 0.005) * decayRate);
                                double remTime = durations[n] - t;
                                if (remTime < 0.025)
                                {
                                    env *= (remTime / 0.025);
                                }
                                double s = (Math.Sin(2.0 * Math.PI * f * t) * 0.75 +
                                            Math.Sin(4.0 * Math.PI * f * t) * 0.20 +
                                            Math.Sin(6.0 * Math.PI * f * t) * 0.05) * env;
                                short sample = (short)(s * 2500);
                                bw.Write(sample);
                            }
                        }

                        // 填充 150ms 纯零静音缓冲
                        for (int i = 0; i < silenceSamples; i++)
                        {
                            bw.Write((short)0);
                        }

                        bw.Flush();
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static void PlayAscendingChime()
        {
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    lock (chimeSyncLock)
                    {
                        if (ascendingPlayer == null)
                        {
                            byte[] wav = GenerateBellChimeWav(new double[] { 784.0, 1046.5 }, new double[] { 0.10, 0.26 });
                            if (wav != null)
                            {
                                var ms = new MemoryStream(wav);
                                ascendingPlayer = new SoundPlayer(ms);
                                ascendingPlayer.Load();
                            }
                        }
                        if (ascendingPlayer != null)
                        {
                            ascendingPlayer.PlaySync();
                        }
                        else
                        {
                            System.Media.SystemSounds.Asterisk.Play();
                        }
                    }
                }
                catch
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Audio.cs: 已忽略异常", diagEx); }
                }
            });
        }

        private static void PlayDescendingChime()
        {
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    lock (chimeSyncLock)
                    {
                        if (descendingPlayer == null)
                        {
                            byte[] wav = GenerateBellChimeWav(new double[] { 1046.5, 784.0 }, new double[] { 0.10, 0.26 });
                            if (wav != null)
                            {
                                var ms = new MemoryStream(wav);
                                descendingPlayer = new SoundPlayer(ms);
                                descendingPlayer.Load();
                            }
                        }
                        if (descendingPlayer != null)
                        {
                            descendingPlayer.PlaySync();
                        }
                        else
                        {
                            System.Media.SystemSounds.Asterisk.Play();
                        }
                    }
                }
                catch
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Audio.cs: 已忽略异常", diagEx); }
                }
            });
        }

    }
}
