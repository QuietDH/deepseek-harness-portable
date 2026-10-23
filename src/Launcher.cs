using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DshUsbLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "DshUsbLauncher.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("DSH 便携版已经在运行，请查看任务栏或窗口列表。", "DSH 便携版",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new LauncherForm());
            }
        }
    }

    public sealed class LauncherForm : Form
    {
        private readonly string root;
        private readonly string homeDir;
        private readonly string agentsDir;
        private readonly string workDir;
        private readonly string logDir;
        private readonly string appZip;
        private readonly string runtimeZip;
        private string cacheBase;
        private string appCache;
        private string runtimeCache;
        private readonly string legacyNode;
        private readonly string legacyBin;

        private readonly object logLock = new object();
        private string nodeExe;
        private string dshBin;
        private Process dsh;
        private StreamWriter logWriter;
        private string logPath;
        private string webUrl;
        private volatile bool ready;
        private volatile bool stopping;
        private volatile bool startFailed;

        private Label lblStatus;
        private Label lblHint;
        private TextBox txtUrl;
        private Button btnOpen;
        private Button btnStop;
        private Button btnData;
        private Button btnLog;
        private ProgressBar bar;

        public LauncherForm()
        {
            root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            homeDir = Path.Combine(root, "home", ".dsh");
            agentsDir = Path.Combine(root, "home", ".agents");
            workDir = Path.Combine(root, "workspace");
            logDir = Path.Combine(root, "logs");
            appZip = Path.Combine(root, "payload", "app.zip");
            runtimeZip = Path.Combine(root, "payload", "runtime.zip");
            legacyNode = Path.Combine(root, "runtime", "node.exe");
            legacyBin = Path.Combine(root, "app", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");

            BuildUi();

            Shown += delegate { ThreadPool.QueueUserWorkItem(delegate { StartDsh(); }); };
            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            Text = "DSH 便携版 · U盘启动器";
            ClientSize = new Size(620, 340);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 252);

            Label title = new Label();
            title.Text = "DeepSeek Harness 便携版";
            title.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(30, 35, 60);
            title.AutoSize = true;
            title.Location = new Point(26, 20);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "免安装 · 换电脑即插即用 · 配置和会话都保存在这个 U 盘里";
            sub.ForeColor = Color.FromArgb(110, 116, 135);
            sub.AutoSize = true;
            sub.Location = new Point(28, 60);
            Controls.Add(sub);

            lblStatus = new Label();
            lblStatus.Text = "正在检查运行环境…";
            lblStatus.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(176, 122, 0);
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(28, 98);
            Controls.Add(lblStatus);

            bar = new ProgressBar();
            bar.Location = new Point(28, 126);
            bar.Size = new Size(564, 6);
            bar.Style = ProgressBarStyle.Marquee;
            Controls.Add(bar);

            txtUrl = new TextBox();
            txtUrl.Location = new Point(28, 144);
            txtUrl.Size = new Size(564, 24);
            txtUrl.ReadOnly = true;
            txtUrl.BorderStyle = BorderStyle.FixedSingle;
            txtUrl.Font = new Font("Consolas", 9F);
            txtUrl.BackColor = Color.White;
            txtUrl.TabStop = false;
            Controls.Add(txtUrl);

            lblHint = new Label();
            lblHint.Text = "首次启动会把运行环境解压到本机（约 1–3 分钟），之后秒开。";
            lblHint.ForeColor = Color.FromArgb(120, 126, 145);
            lblHint.AutoSize = true;
            lblHint.Location = new Point(28, 176);
            Controls.Add(lblHint);

            btnOpen = MakeButton("打开界面", 28, 212, 132, 38, true);
            btnOpen.Enabled = false;
            btnOpen.Click += delegate { OpenBrowser(); };
            Controls.Add(btnOpen);

            btnData = MakeButton("打开数据文件夹", 172, 212, 132, 38, false);
            btnData.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + root + "\""); }
                catch (Exception ex) { MessageBox.Show(this, "无法打开文件夹：" + ex.Message, "DSH 便携版"); }
            };
            Controls.Add(btnData);

            btnLog = MakeButton("查看日志", 316, 212, 116, 38, false);
            btnLog.Click += delegate
            {
                if (logPath != null && File.Exists(logPath))
                {
                    try { Process.Start("notepad.exe", "\"" + logPath + "\""); return; }
                    catch (Exception) { }
                }
                MessageBox.Show(this, "还没有日志文件。", "DSH 便携版");
            };
            Controls.Add(btnLog);

            btnStop = MakeButton("停止并退出", 444, 212, 148, 38, false);
            btnStop.Click += delegate
            {
                if (dsh == null || (dsh != null && dsh.HasExited))
                {
                    Close();
                    return;
                }
                DialogResult r = MessageBox.Show(this, "停止 DSH 并关闭启动器？", "DSH 便携版",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) Close();
            };
            Controls.Add(btnStop);

            Label foot = new Label();
            foot.Text = "提示：U 盘盘符变了也能用；关闭此窗口会停止 DSH。";
            foot.ForeColor = Color.FromArgb(140, 146, 165);
            foot.AutoSize = true;
            foot.Location = new Point(28, 276);
            Controls.Add(foot);

            AcceptButton = btnOpen;
        }

        private Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(210, 214, 226);
            if (primary)
            {
                b.BackColor = Color.FromArgb(64, 92, 230);
                b.ForeColor = Color.White;
                b.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            }
            else
            {
                b.BackColor = Color.White;
                b.ForeColor = Color.FromArgb(50, 56, 75);
            }
            b.UseVisualStyleBackColor = false;
            return b;
        }

        private void StartDsh()
        {
            try
            {
                Directory.CreateDirectory(homeDir);
                Directory.CreateDirectory(agentsDir);
                Directory.CreateDirectory(workDir);
                Directory.CreateDirectory(logDir);

                logPath = Path.Combine(logDir, "dsh-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                logWriter = new StreamWriter(logPath, true, new UTF8Encoding(false));
                logWriter.AutoFlush = true;
                Log("launcher: root=" + root);

                if (!ResolveCache()) return;
                if (!ResolveRuntime()) return;
                if (!ResolveApp()) return;

                Log("launcher: node=" + nodeExe);
                Log("launcher: bin=" + dshBin);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = nodeExe;
                psi.Arguments = "\"" + dshBin + "\" web --port 0 --host 127.0.0.1 --no-open";
                psi.WorkingDirectory = workDir;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["DSH_HOME"] = homeDir;
                psi.EnvironmentVariables["DSH_AGENTS_HOME"] = agentsDir;
                psi.EnvironmentVariables["DSH_TELEMETRY_DISABLED"] = "1";
                psi.EnvironmentVariables["PATH"] = Path.GetDirectoryName(nodeExe) + ";" + Environment.GetEnvironmentVariable("PATH");

                dsh = new Process();
                dsh.StartInfo = psi;
                dsh.EnableRaisingEvents = true;
                dsh.Exited += delegate { OnDshExited(); };
                dsh.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { OnLine(e.Data, false); };
                dsh.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { OnLine(e.Data, true); };
                dsh.Start();
                dsh.BeginOutputReadLine();
                dsh.BeginErrorReadLine();
                Log("launcher: dsh pid=" + dsh.Id);

                Ui(delegate
                {
                    SetStatus("正在启动 DSH，请稍候…", Color.FromArgb(176, 122, 0));
                    bar.Style = ProgressBarStyle.Marquee;
                });
                StartWatchdog();
            }
            catch (Exception ex)
            {
                Fail("启动失败：" + ex.Message);
            }
        }

        private bool ResolveCache()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHP"),
                Path.Combine(Path.GetTempPath(), "DSHP"),
                Path.Combine(root, ".cache")
            };
            int index = 0;
            foreach (string candidate in candidates)
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".write-probe");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    cacheBase = candidate;
                    appCache = Path.Combine(cacheBase, "a");
                    runtimeCache = Path.Combine(cacheBase, "r");
                    Log("launcher: cache=" + cacheBase);
                    if (index > 0) Log("launcher: fallback cache in use (candidate " + index + ")");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("launcher: cache candidate failed: " + candidate + " : " + ex.Message);
                }
                index++;
            }
            Fail("无法创建本机缓存目录（已尝试 LocalAppData、临时目录和程序目录）。\n" +
                 "如果是从 DSH 聊天界面里打开的，请到资源管理器里双击 DSH-Portable.exe 再试；\n" +
                 "也请检查杀毒软件是否拦截了本程序写文件。");
            return false;
        }

        private bool ResolveRuntime()
        {
            if (File.Exists(runtimeZip))
            {
                string stamp = Stamp(runtimeZip);
                string marker = Path.Combine(runtimeCache, ".stamp");
                string node = Path.Combine(runtimeCache, "node.exe");
                if (!File.Exists(node) || !MarkerMatches(marker, stamp))
                {
                    Log("launcher: extracting runtime " + runtimeZip);
                    TryDeleteDir(runtimeCache);
                    ExtractZip(runtimeZip, runtimeCache, "正在解压 Node 运行环境", false);
                    File.WriteAllText(marker, stamp);
                }
                nodeExe = node;
                Log("launcher: runtime=" + nodeExe);
                return true;
            }
            if (File.Exists(legacyNode))
            {
                nodeExe = legacyNode;
                Log("launcher: runtime=USB " + nodeExe);
                return true;
            }
            Fail("找不到 Node 运行环境：payload\\runtime.zip 和 runtime\\node.exe 都不存在。");
            return false;
        }

        private bool ResolveApp()
        {
            if (File.Exists(appZip))
            {
                string stamp = Stamp(appZip);
                string marker = Path.Combine(appCache, ".stamp");
                string bin = Path.Combine(appCache, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                if (!File.Exists(bin) || !MarkerMatches(marker, stamp))
                {
                    Log("launcher: extracting app " + appZip);
                    long t0 = DateTime.UtcNow.Ticks;
                    try
                    {
                        TryDeleteDir(appCache);
                        ExtractZip(appZip, appCache, "正在准备 DSH 运行环境", true);
                        File.WriteAllText(marker, stamp);
                        Log("launcher: extract done in " + Math.Round((DateTime.UtcNow.Ticks - t0) / 10000000.0, 1) + "s");
                    }
                    catch (Exception ex)
                    {
                        Log("launcher: extract failed: " + ex);
                        if (File.Exists(legacyBin))
                        {
                            Log("launcher: falling back to USB app dir");
                            dshBin = legacyBin;
                            Ui(delegate { SetStatus("解压失败，改用 U 盘内的程序文件（会慢一些）…", Color.FromArgb(176, 122, 0)); });
                            return true;
                        }
                        Fail("解压 DSH 运行环境失败：" + ex.Message);
                        return false;
                    }
                }
                dshBin = bin;
                Log("launcher: app=" + appCache);
                return true;
            }
            if (File.Exists(legacyBin))
            {
                dshBin = legacyBin;
                Log("launcher: app=USB " + legacyBin);
                return true;
            }
            Fail("找不到 DSH 程序：payload\\app.zip 和 app\\node_modules 都不存在。");
            return false;
        }

        private void ExtractZip(string zipFile, string destDir, string label, bool showProgress)
        {
            Directory.CreateDirectory(destDir);
            string destFull = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (ZipArchive zip = ZipFile.OpenRead(zipFile))
            {
                int total = zip.Entries.Count;
                int done = 0;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    if (relative.Length == 0 || relative == ".") continue;
                    string target = Path.GetFullPath(Path.Combine(destDir, relative));
                    if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("压缩包包含非法路径：" + entry.FullName);
                    if (relative.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        string parent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        entry.ExtractToFile(target, true);
                    }
                    done++;
                    if (showProgress && total > 0 && done % 500 == 0)
                    {
                        int pct = (int)(done * 100L / total);
                        string text = label + " " + pct + "%";
                        Ui(delegate
                        {
                            SetStatus(text + "…", Color.FromArgb(176, 122, 0));
                            bar.Style = ProgressBarStyle.Continuous;
                            if (pct >= 0 && pct <= 100) bar.Value = pct;
                        });
                    }
                }
            }
            if (showProgress)
            {
                Ui(delegate
                {
                    bar.Style = ProgressBarStyle.Marquee;
                    bar.Value = 0;
                });
            }
        }

        private static string Stamp(string file)
        {
            FileInfo fi = new FileInfo(file);
            return fi.Length.ToString() + "-" + fi.LastWriteTimeUtc.Ticks.ToString();
        }

        private static bool MarkerMatches(string markerPath, string stamp)
        {
            try
            {
                return File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == stamp;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryDeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception) { }
        }

        private void OnLine(string raw, bool isError)
        {
            if (raw == null) return;
            string line = StripAnsi(raw);
            Log((isError ? "err: " : "out: ") + line);
            if (ready) return;
            Match m = Regex.Match(line, @"dsh web:\s*(https?://\S+)");
            if (m.Success)
            {
                webUrl = m.Groups[1].Value.Trim();
                MarkReady();
            }
        }

        private void MarkReady()
        {
            if (ready) return;
            ready = true;
            Ui(delegate
            {
                SetStatus("DSH 已就绪，正在打开浏览器…", Color.FromArgb(0, 128, 96));
                txtUrl.Text = webUrl;
                btnOpen.Enabled = true;
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 100;
                lblHint.Text = "关掉浏览器不影响运行；要停止 DSH，请点“停止并退出”。";
            });
            ThreadPool.QueueUserWorkItem(delegate { WaitHttpAndOpen(); });
        }

        private void WaitHttpAndOpen()
        {
            for (int i = 0; i < 40; i++)
            {
                if (stopping) return;
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(webUrl);
                    req.Timeout = 2000;
                    req.Method = "GET";
                    req.AllowAutoRedirect = true;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        if (code >= 200 && code < 400)
                        {
                            OpenBrowser();
                            return;
                        }
                    }
                }
                catch (WebException) { }
                catch (Exception) { }
                Thread.Sleep(500);
            }
            Ui(delegate
            {
                SetStatus("DSH 已启动，但页面还没响应，请手动点“打开界面”。", Color.FromArgb(176, 122, 0));
            });
        }

        private void OpenBrowser()
        {
            if (Environment.GetEnvironmentVariable("DSH_LAUNCHER_NO_BROWSER") == "1")
            {
                Log("launcher: browser opening disabled by DSH_LAUNCHER_NO_BROWSER");
                return;
            }
            if (string.IsNullOrEmpty(webUrl))
            {
                MessageBox.Show(this, "DSH 还没有启动完成。", "DSH 便携版", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
                Log("launcher: opened browser " + webUrl);
                Ui(delegate
                {
                    SetStatus("DSH 已就绪", Color.FromArgb(0, 128, 96));
                    lblHint.Text = "浏览器已打开；关掉浏览器不影响运行，要停止请点“停止并退出”。";
                });
            }
            catch (Exception ex)
            {
                Ui(delegate { SetStatus("无法自动打开浏览器，请手动复制上面的网址。", Color.Firebrick); });
                MessageBox.Show(this, "无法打开浏览器：" + ex.Message + "\n请手动复制上面的网址打开。", "DSH 便携版");
            }
        }

        private void StartWatchdog()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < 600 && !ready && !stopping && !startFailed; i++)
                {
                    Process p = dsh;
                    if (p == null) return;
                    try { if (p.HasExited) return; }
                    catch (Exception) { return; }
                    Thread.Sleep(500);
                }
                if (!ready && !stopping && !startFailed)
                {
                    Ui(delegate { SetStatus("启动超时（5 分钟），请点“查看日志”。", Color.Firebrick); });
                }
            });
        }

        private void OnDshExited()
        {
            if (stopping) return;
            int code = -1;
            try { if (dsh != null) code = dsh.ExitCode; }
            catch (Exception) { }
            Log("launcher: dsh exited code=" + code);
            Ui(delegate
            {
                ready = false;
                btnOpen.Enabled = false;
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 0;
                SetStatus("DSH 已退出（代码 " + code + "），请查看日志。", Color.Firebrick);
            });
        }

        private void StopDsh()
        {
            stopping = true;
            Process p = dsh;
            dsh = null;
            if (p != null)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        bool killed = false;
                        try
                        {
                            ProcessStartInfo tk = new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F");
                            tk.UseShellExecute = false;
                            tk.CreateNoWindow = true;
                            using (Process t = Process.Start(tk))
                            {
                                t.WaitForExit(4000);
                                killed = t.HasExited;
                            }
                        }
                        catch (Exception) { }
                        if (!killed)
                        {
                            try { p.Kill(); }
                            catch (Exception) { }
                        }
                        try { p.WaitForExit(4000); }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
                try { p.Dispose(); }
                catch (Exception) { }
            }
            Log("launcher: stopped");
            Ui(delegate
            {
                ready = false;
                SetStatus("DSH 已停止。", Color.FromArgb(110, 116, 135));
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 0;
                btnOpen.Enabled = false;
            });
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            bool running = false;
            try { running = dsh != null && !dsh.HasExited; }
            catch (Exception) { }
            if (running)
            {
                DialogResult r = MessageBox.Show(this, "退出会停止 DSH，确定吗？", "DSH 便携版",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (r != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                StopDsh();
            }
            else
            {
                stopping = true;
            }
            lock (logLock)
            {
                if (logWriter != null)
                {
                    try { logWriter.Flush(); logWriter.Dispose(); }
                    catch (Exception) { }
                    logWriter = null;
                }
            }
        }

        private void Fail(string message)
        {
            startFailed = true;
            Log("launcher: " + message);
            Ui(delegate
            {
                SetStatus(message, Color.Firebrick);
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 0;
                MessageBox.Show(this, message + "\n\n日志：" + (logPath ?? "(无)"), "DSH 便携版",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private void Ui(MethodInvoker action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void Log(string message)
        {
            lock (logLock)
            {
                if (logWriter == null) return;
                try
                {
                    logWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
                }
                catch (Exception) { }
            }
        }

        private static string StripAnsi(string text)
        {
            return Regex.Replace(text, "\u001b\\[[0-9;?]*[ -/]*[@-~]", "");
        }
    }
}
