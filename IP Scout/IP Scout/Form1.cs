using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IP_Scout
{
    public partial class Form1 : Form
    {
        private ConcurrentBag<ScanResult> scanResults = new ConcurrentBag<ScanResult>();
        private ContextMenuStrip contextMenu;

        private bool showDeadHosts = false;
        private bool showAliveOnly = true;
        private bool showWithPortsOnly = false;

        private int maxThreads = 100;
        private int threadDelay = 20;

        private int pingProbes = 3;
        private int pingTimeout = 2000;
        private bool scanDeadHosts = false;

        private bool skipUnassigned = true;

        private string customPorts = "21,22,23,80,443,445,3389,5900,8080";
        private int portTimeout = 500;
        private bool adaptTimeout = true;
        private int minAdaptedTimeout = 100;

        private bool askConfirmation = false;
        private bool showInfoAfterScan = true;

        private Panel networkMapPanel;
        private ScanResult hoveredHost = null;
        private Point mousePosition;
        private Timer mapRefreshTimer;
        private float zoomLevel = 1.0f;
        private ScanResult selectedHostForContextMenu = null;

        public Form1()
        {
            InitializeComponent();
            SetupDataGridView();
            SetupContextMenu();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            txtStartIP.Text = "192.168.2.0";
            txtEndIP.Text = "192.168.2.255";
            SetupNetworkMapTab();
        }

        private void SetupNetworkMapTab()
        {
            TabControl mainTabs = new TabControl()
            {
                Left = 12,
                Top = 70,
                Width = 800,
                Height = 512
            };

            TabPage listTab = new TabPage("Results List");
            dgvResults.Parent = listTab;
            dgvResults.Left = 0;
            dgvResults.Top = 0;
            dgvResults.Width = 793;
            dgvResults.Height = 483;
            mainTabs.TabPages.Add(listTab);

            TabPage mapTab = new TabPage("Network Map");

            DoubleBufferedPanel mapPanel = new DoubleBufferedPanel()
            {
                Left = 0,
                Top = 0,
                Width = 793,
                Height = 483,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = false
            };
            mapPanel.Paint += MapPanel_Paint;
            mapPanel.MouseClick += MapPanel_MouseClick;
            mapPanel.MouseMove += MapPanel_MouseMove;
            mapPanel.MouseWheel += MapPanel_MouseWheel;

            mapTab.Controls.Add(mapPanel);
            mainTabs.TabPages.Add(mapTab);

            this.Controls.Add(mainTabs);

            networkMapPanel = mapPanel;

            mapRefreshTimer = new Timer();
            mapRefreshTimer.Interval = 100;
            mapRefreshTimer.Tick += (s, e) => networkMapPanel?.Invalidate();
        }

        private void MapPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int centerX = networkMapPanel.Width / 2;
            int centerY = networkMapPanel.Height / 2;

            DrawRadarRings(g, centerX, centerY);

            var hosts = scanResults.Where(h => ShouldDisplayHost(h)).ToList();

            if (hosts.Count == 0)
            {
                DrawEmptyState(g, centerX, centerY);
                return;
            }

            long maxPing = Math.Max(1, hosts.Where(h => h.online).Any() ? hosts.Where(h => h.online).Max(h => h.ping) : 1);

            foreach (var host in hosts.Where(h => h.online))
            {
                var pos = CalculatePosition(centerX, centerY, host.ping, maxPing, hosts.IndexOf(host), hosts.Count);
                DrawConnection(g, centerX, centerY, pos.X, pos.Y, host);
            }

            foreach (var host in hosts)
            {
                if (!host.online && !ShouldDisplayHost(host)) continue;

                var pos = CalculatePosition(centerX, centerY, host.ping, maxPing, hosts.IndexOf(host), hosts.Count);
                DrawDevice(g, pos.X, pos.Y, host, host == hoveredHost);
            }

            DrawCurrentDevice(g, centerX, centerY);

            if (hoveredHost != null)
            {
                DrawTooltip(g, hoveredHost, mousePosition);
            }

            DrawLegend(g);
        }

        private void DrawRadarRings(Graphics g, int centerX, int centerY)
        {
            int maxRadius = (int)((Math.Min(centerX, centerY) - 60) * zoomLevel);

            using (Pen ringPen = new Pen(Color.FromArgb(220, 220, 220), 1))
            {
                for (int i = 1; i <= 4; i++)
                {
                    int radius = (maxRadius * i) / 4;
                    g.DrawEllipse(ringPen, centerX - radius, centerY - radius, radius * 2, radius * 2);
                }
            }

            using (Pen crossPen = new Pen(Color.FromArgb(200, 200, 200), 1))
            {
                crossPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawLine(crossPen, centerX - maxRadius, centerY, centerX + maxRadius, centerY);
                g.DrawLine(crossPen, centerX, centerY - maxRadius, centerX, centerY + maxRadius);
            }

            using (Font labelFont = new Font("Segoe UI", 8))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
            {
                for (int i = 1; i <= 4; i++)
                {
                    int radius = (maxRadius * i) / 4;
                    string label = $"{(i * 50)}ms";
                    var size = g.MeasureString(label, labelFont);
                    g.DrawString(label, labelFont, labelBrush, centerX + radius + 5, centerY - size.Height / 2);
                }
            }
        }

        private void DrawEmptyState(Graphics g, int centerX, int centerY)
        {
            DrawCurrentDevice(g, centerX, centerY);

            string message = "No network data\n\nStart a scan to see devices";
            using (Font font = new Font("Segoe UI", 11))
            using (SolidBrush brush = new SolidBrush(Color.Gray))
            {
                SizeF size = g.MeasureString(message, font);
                g.DrawString(message, font, brush,
                    centerX - size.Width / 2,
                    centerY + 80);
            }
        }

        private Point CalculatePosition(int centerX, int centerY, long ping, long maxPing, int index, int totalCount)
        {
            int maxRadius = (int)((Math.Min(centerX, centerY) - 80) * zoomLevel);
            float normalizedPing = Math.Min(1.0f, (float)ping / Math.Max(1, maxPing));
            int distance = (int)(40 + (maxRadius - 40) * normalizedPing);

            double angle = (2 * Math.PI * index / Math.Max(1, totalCount)) + (Math.PI / 4);

            int x = centerX + (int)(distance * Math.Cos(angle));
            int y = centerY + (int)(distance * Math.Sin(angle));

            return new Point(x, y);
        }

        private void DrawConnection(Graphics g, int centerX, int centerY, int x, int y, ScanResult host)
        {
            Color lineColor;
            if (host.portStatus == PortStatus.Open)
                lineColor = Color.FromArgb(100, 0, 200, 0);
            else
                lineColor = Color.FromArgb(100, 0, 120, 215);

            using (Pen linePen = new Pen(lineColor, 1))
            {
                linePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                g.DrawLine(linePen, centerX, centerY, x, y);
            }
        }

        private void DrawCurrentDevice(Graphics g, int centerX, int centerY)
        {
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            {
                g.FillEllipse(shadowBrush, centerX - 33, centerY - 31, 66, 66);
            }

            using (SolidBrush circleBrush = new SolidBrush(Color.FromArgb(0, 120, 215)))
            {
                g.FillEllipse(circleBrush, centerX - 30, centerY - 30, 60, 60);
            }

            using (Pen borderPen = new Pen(Color.FromArgb(0, 100, 180), 2))
            {
                g.DrawEllipse(borderPen, centerX - 30, centerY - 30, 60, 60);
            }

            using (SolidBrush iconBrush = new SolidBrush(Color.White))
            {
                Rectangle screen = new Rectangle(centerX - 12, centerY - 12, 24, 16);
                g.FillRectangle(iconBrush, screen);

                Point[] baseShape = new Point[]
                {
                    new Point(centerX - 16, centerY + 5),
                    new Point(centerX + 16, centerY + 5),
                    new Point(centerX + 14, centerY + 9),
                    new Point(centerX - 14, centerY + 9)
                };
                g.FillPolygon(iconBrush, baseShape);
            }

            using (Font labelFont = new Font("Segoe UI", 8, FontStyle.Bold))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
            {
                string label = "YOU";
                SizeF size = g.MeasureString(label, labelFont);
                g.DrawString(label, labelFont, labelBrush,
                    centerX - size.Width / 2,
                    centerY + 35);
            }
        }

        private void DrawDevice(Graphics g, int x, int y, ScanResult host, bool isHovered)
        {
            int nodeSize = isHovered ? 38 : 32;
            int halfSize = nodeSize / 2;

            Color nodeColor;
            Color borderColor;

            if (!host.online)
            {
                nodeColor = Color.FromArgb(220, 53, 69);
                borderColor = Color.FromArgb(180, 40, 55);
            }
            else if (host.portStatus == PortStatus.Open)
            {
                nodeColor = Color.FromArgb(40, 167, 69);
                borderColor = Color.FromArgb(30, 130, 55);
            }
            else
            {
                nodeColor = Color.FromArgb(0, 120, 215);
                borderColor = Color.FromArgb(0, 100, 180);
            }

            if (isHovered)
            {
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                {
                    g.FillEllipse(shadowBrush, x - halfSize + 2, y - halfSize + 2, nodeSize, nodeSize);
                }
            }

            using (SolidBrush brush = new SolidBrush(nodeColor))
            {
                g.FillEllipse(brush, x - halfSize, y - halfSize, nodeSize, nodeSize);
            }

            using (Pen pen = new Pen(borderColor, isHovered ? 2 : 1))
            {
                g.DrawEllipse(pen, x - halfSize, y - halfSize, nodeSize, nodeSize);
            }

            string symbol = GetDeviceIcon(host);
            using (Font symbolFont = new Font("Segoe UI", isHovered ? 14 : 12))
            using (SolidBrush symbolBrush = new SolidBrush(Color.White))
            {
                SizeF symbolSize = g.MeasureString(symbol, symbolFont);
                g.DrawString(symbol, symbolFont, symbolBrush,
                    x - symbolSize.Width / 2,
                    y - symbolSize.Height / 2);
            }

            using (Font ipFont = new Font("Segoe UI", 7))
            using (SolidBrush ipBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
            {
                string ipText = host.ip.Split('.')[3];
                SizeF ipSize = g.MeasureString(ipText, ipFont);
                g.DrawString(ipText, ipFont, ipBrush,
                    x - ipSize.Width / 2,
                    y + halfSize + 3);
            }

            if (host.online)
            {
                using (Font pingFont = new Font("Segoe UI", 6))
                using (SolidBrush pingBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                {
                    string pingText = $"{host.ping}ms";
                    SizeF pingSize = g.MeasureString(pingText, pingFont);
                    g.DrawString(pingText, pingFont, pingBrush,
                        x - pingSize.Width / 2,
                        y + halfSize + 13);
                }
            }
        }

        private string GetDeviceIcon(ScanResult host)
        {
            if (!host.online) return "✕";

            if (host.openPorts.Contains("80") || host.openPorts.Contains("443"))
                return "🌐";
            else if (host.openPorts.Contains("22"))
                return "🐧";
            else if (host.openPorts.Contains("3389"))
                return "🖥";
            else if (host.openPorts.Contains("445"))
                return "💾";
            else if (host.portStatus == PortStatus.Open)
                return "✓";
            else
                return "○";
        }

        private void DrawTooltip(Graphics g, ScanResult host, Point mouse)
        {
            string tooltip = $"IP: {host.ip}\n";
            tooltip += $"Status: {(host.online ? "Online" : "Offline")}\n";

            if (host.online)
            {
                tooltip += $"Ping: {host.ping}ms\n";
                tooltip += $"Hostname: {host.hostname}\n";
                tooltip += $"Ports: {host.openPorts}";
            }

            using (Font tooltipFont = new Font("Segoe UI", 9))
            {
                SizeF tooltipSize = g.MeasureString(tooltip, tooltipFont);

                int padding = 8;
                int tooltipX = mouse.X + 15;
                int tooltipY = mouse.Y + 15;

                if (tooltipX + tooltipSize.Width + padding * 2 > networkMapPanel.Width)
                    tooltipX = mouse.X - (int)tooltipSize.Width - padding * 2 - 15;
                if (tooltipY + tooltipSize.Height + padding * 2 > networkMapPanel.Height)
                    tooltipY = mouse.Y - (int)tooltipSize.Height - padding * 2 - 15;

                Rectangle tooltipRect = new Rectangle(
                    tooltipX, tooltipY,
                    (int)tooltipSize.Width + padding * 2,
                    (int)tooltipSize.Height + padding * 2
                );

                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                {
                    g.FillRectangle(shadowBrush, tooltipRect.X + 2, tooltipRect.Y + 2, tooltipRect.Width, tooltipRect.Height);
                }

                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(255, 255, 255)))
                {
                    g.FillRectangle(bgBrush, tooltipRect);
                }

                using (Pen borderPen = new Pen(Color.FromArgb(180, 180, 180), 1))
                {
                    g.DrawRectangle(borderPen, tooltipRect);
                }

                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                {
                    g.DrawString(tooltip, tooltipFont, textBrush, tooltipX + padding, tooltipY + padding);
                }
            }
        }

        private void DrawLegend(Graphics g)
        {
            int x = 10;
            int y = networkMapPanel.Height - 85;

            using (Font legendFont = new Font("Segoe UI", 8))
            {
                using (SolidBrush greenBrush = new SolidBrush(Color.FromArgb(40, 167, 69)))
                {
                    g.FillEllipse(greenBrush, x, y, 12, 12);
                }
                using (Pen greenPen = new Pen(Color.FromArgb(30, 130, 55), 1))
                {
                    g.DrawEllipse(greenPen, x, y, 12, 12);
                }
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                {
                    g.DrawString("Open Ports", legendFont, textBrush, x + 18, y);
                }

                y += 20;
                using (SolidBrush blueBrush = new SolidBrush(Color.FromArgb(0, 120, 215)))
                {
                    g.FillEllipse(blueBrush, x, y, 12, 12);
                }
                using (Pen bluePen = new Pen(Color.FromArgb(0, 100, 180), 1))
                {
                    g.DrawEllipse(bluePen, x, y, 12, 12);
                }
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                {
                    g.DrawString("Alive", legendFont, textBrush, x + 18, y);
                }

                y += 20;
                using (SolidBrush redBrush = new SolidBrush(Color.FromArgb(220, 53, 69)))
                {
                    g.FillEllipse(redBrush, x, y, 12, 12);
                }
                using (Pen redPen = new Pen(Color.FromArgb(180, 40, 55), 1))
                {
                    g.DrawEllipse(redPen, x, y, 12, 12);
                }
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                {
                    g.DrawString("Dead", legendFont, textBrush, x + 18, y);
                }

                y += 25;
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                {
                    g.DrawString($"Zoom: {(int)(zoomLevel * 100)}%", legendFont, textBrush, x, y);
                }
            }
        }

        private void MapPanel_MouseMove(object sender, MouseEventArgs e)
        {
            mousePosition = e.Location;

            var hosts = scanResults.Where(h => ShouldDisplayHost(h)).ToList();
            if (hosts.Count == 0) return;

            int centerX = networkMapPanel.Width / 2;
            int centerY = networkMapPanel.Height / 2;

            long maxPing = Math.Max(1, hosts.Where(h => h.online).Any() ? hosts.Where(h => h.online).Max(h => h.ping) : 1);

            ScanResult newHover = null;

            foreach (var host in hosts)
            {
                var pos = CalculatePosition(centerX, centerY, host.ping, maxPing, hosts.IndexOf(host), hosts.Count);
                int distance = (int)Math.Sqrt(Math.Pow(e.X - pos.X, 2) + Math.Pow(e.Y - pos.Y, 2));

                if (distance < 20)
                {
                    newHover = host;
                    break;
                }
            }

            if (newHover != hoveredHost)
            {
                hoveredHost = newHover;
                networkMapPanel?.Invalidate();
            }
        }

        private void MapPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && hoveredHost != null)
            {
                selectedHostForContextMenu = hoveredHost;
                contextMenu.Show(networkMapPanel, e.Location);
            }
        }

        private void MapPanel_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
            {
                zoomLevel = Math.Min(3.0f, zoomLevel + 0.1f);
            }
            else
            {
                zoomLevel = Math.Max(0.5f, zoomLevel - 0.1f);
            }

            networkMapPanel?.Invalidate();
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            Form settingsForm = new Form()
            {
                Text = "Settings",
                Size = new Size(520, 490),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            TabControl tabControl = new TabControl()
            {
                Left = 10,
                Top = 10,
                Width = 485,
                Height = 370
            };

            TabPage scanTab = new TabPage("Scanning");

            GroupBox threadsGroup = new GroupBox()
            {
                Text = "Threads",
                Left = 10,
                Top = 10,
                Width = 450,
                Height = 100
            };

            Label delayLabel = new Label()
            {
                Text = "Delay between starting threads (in ms):",
                Left = 10,
                Top = 25,
                Width = 250,
                Height = 20
            };
            threadsGroup.Controls.Add(delayLabel);

            NumericUpDown numDelay = new NumericUpDown()
            {
                Left = 270,
                Top = 23,
                Width = 80,
                Minimum = 0,
                Maximum = 1000,
                Value = threadDelay
            };
            threadsGroup.Controls.Add(numDelay);

            Label maxThreadsLabel = new Label()
            {
                Text = "Maximum number of threads:",
                Left = 10,
                Top = 60,
                Width = 250,
                Height = 20
            };
            threadsGroup.Controls.Add(maxThreadsLabel);

            NumericUpDown numMaxThreads = new NumericUpDown()
            {
                Left = 270,
                Top = 58,
                Width = 80,
                Minimum = 10,
                Maximum = 500,
                Value = maxThreads
            };
            threadsGroup.Controls.Add(numMaxThreads);

            scanTab.Controls.Add(threadsGroup);

            GroupBox pingGroup = new GroupBox()
            {
                Text = "Pinging",
                Left = 10,
                Top = 120,
                Width = 450,
                Height = 150
            };

            Label probesLabel = new Label()
            {
                Text = "Number of ping probes (packets to send):",
                Left = 10,
                Top = 25,
                Width = 250,
                Height = 20
            };
            pingGroup.Controls.Add(probesLabel);

            NumericUpDown numProbes = new NumericUpDown()
            {
                Left = 270,
                Top = 23,
                Width = 80,
                Minimum = 1,
                Maximum = 10,
                Value = pingProbes
            };
            pingGroup.Controls.Add(numProbes);

            Label timeoutLabel = new Label()
            {
                Text = "Ping timeout (in ms):",
                Left = 10,
                Top = 60,
                Width = 250,
                Height = 20
            };
            pingGroup.Controls.Add(timeoutLabel);

            NumericUpDown numPingTimeout = new NumericUpDown()
            {
                Left = 270,
                Top = 58,
                Width = 80,
                Minimum = 500,
                Maximum = 10000,
                Increment = 500,
                Value = pingTimeout
            };
            pingGroup.Controls.Add(numPingTimeout);

            CheckBox chkScanDead = new CheckBox()
            {
                Text = "Scan dead hosts, which don't reply to pings",
                Left = 10,
                Top = 95,
                Width = 420,
                Height = 25,
                Checked = scanDeadHosts
            };
            pingGroup.Controls.Add(chkScanDead);

            scanTab.Controls.Add(pingGroup);

            GroupBox skipGroup = new GroupBox()
            {
                Text = "Skipping",
                Left = 10,
                Top = 280,
                Width = 450,
                Height = 60
            };

            CheckBox chkSkipUnassigned = new CheckBox()
            {
                Text = "Skip probably unassigned IP addresses *.0 and *.255",
                Left = 10,
                Top = 25,
                Width = 420,
                Height = 25,
                Checked = skipUnassigned
            };
            skipGroup.Controls.Add(chkSkipUnassigned);

            scanTab.Controls.Add(skipGroup);

            tabControl.TabPages.Add(scanTab);

            TabPage portsTab = new TabPage("Ports");

            GroupBox timingGroup = new GroupBox()
            {
                Text = "Timing",
                Left = 10,
                Top = 10,
                Width = 450,
                Height = 130
            };

            Label portTimeoutLabel = new Label()
            {
                Text = "Default port connect timeout (in ms):",
                Left = 10,
                Top = 25,
                Width = 250,
                Height = 20
            };
            timingGroup.Controls.Add(portTimeoutLabel);

            NumericUpDown numPortTimeout = new NumericUpDown()
            {
                Left = 270,
                Top = 23,
                Width = 80,
                Minimum = 100,
                Maximum = 10000,
                Increment = 100,
                Value = portTimeout
            };
            timingGroup.Controls.Add(numPortTimeout);

            CheckBox chkAdaptTimeout = new CheckBox()
            {
                Text = "Adapt timeout to ping roundtrip time (if available)",
                Left = 10,
                Top = 55,
                Width = 420,
                Height = 25,
                Checked = adaptTimeout
            };
            timingGroup.Controls.Add(chkAdaptTimeout);

            Label minTimeoutLabel = new Label()
            {
                Text = "Minimal adapted connect timeout (in ms):",
                Left = 10,
                Top = 90,
                Width = 250,
                Height = 20
            };
            timingGroup.Controls.Add(minTimeoutLabel);

            NumericUpDown numMinTimeout = new NumericUpDown()
            {
                Left = 270,
                Top = 88,
                Width = 80,
                Minimum = 50,
                Maximum = 5000,
                Increment = 50,
                Value = minAdaptedTimeout
            };
            timingGroup.Controls.Add(numMinTimeout);

            portsTab.Controls.Add(timingGroup);

            GroupBox portSelGroup = new GroupBox()
            {
                Text = "Port selection",
                Left = 10,
                Top = 150,
                Width = 450,
                Height = 180
            };

            Label portInfoLabel = new Label()
            {
                Text = "Specify ports to scan here. Ranges are supported.\nExample: 1-3,5,7,10-15,6000-6100\n\nIf many ports are specified, scanning can take a lot of time.",
                Left = 10,
                Top = 20,
                Width = 420,
                Height = 70,
                AutoSize = false
            };
            portSelGroup.Controls.Add(portInfoLabel);

            TextBox txtPorts = new TextBox()
            {
                Left = 10,
                Top = 95,
                Width = 420,
                Height = 25,
                Text = customPorts,
                Font = new Font("Consolas", 9)
            };
            portSelGroup.Controls.Add(txtPorts);

            Label portHintLabel = new Label()
            {
                Text = "Common: 21(FTP), 22(SSH), 80(HTTP), 443(HTTPS), 445(SMB), 3389(RDP)",
                Left = 10,
                Top = 130,
                Width = 420,
                Height = 40,
                ForeColor = Color.Gray,
                AutoSize = false
            };
            portSelGroup.Controls.Add(portHintLabel);

            portsTab.Controls.Add(portSelGroup);

            tabControl.TabPages.Add(portsTab);

            TabPage displayTab = new TabPage("Display");

            GroupBox displayGroup = new GroupBox()
            {
                Text = "Display in the results list",
                Left = 10,
                Top = 10,
                Width = 450,
                Height = 130
            };

            RadioButton rbShowAll = new RadioButton()
            {
                Text = "All scanned hosts",
                Left = 15,
                Top = 25,
                Width = 400,
                Height = 25,
                Checked = showDeadHosts && !showAliveOnly && !showWithPortsOnly
            };
            displayGroup.Controls.Add(rbShowAll);

            RadioButton rbAliveOnly = new RadioButton()
            {
                Text = "Alive hosts (responding to pings) only",
                Left = 15,
                Top = 55,
                Width = 400,
                Height = 25,
                Checked = showAliveOnly && !showWithPortsOnly
            };
            displayGroup.Controls.Add(rbAliveOnly);

            RadioButton rbPortsOnly = new RadioButton()
            {
                Text = "Hosts with open ports only",
                Left = 15,
                Top = 85,
                Width = 400,
                Height = 25,
                Checked = showWithPortsOnly
            };
            displayGroup.Controls.Add(rbPortsOnly);

            displayTab.Controls.Add(displayGroup);

            GroupBox labelsGroup = new GroupBox()
            {
                Text = "Labels displayed in the results list",
                Left = 10,
                Top = 150,
                Width = 450,
                Height = 100
            };

            Label colorLabel = new Label()
            {
                Text = "Red = Dead/Offline\nBlue = Alive, no open ports\nGreen = Alive with open ports",
                Left = 15,
                Top = 25,
                Width = 420,
                Height = 65,
                AutoSize = false
            };
            labelsGroup.Controls.Add(colorLabel);

            displayTab.Controls.Add(labelsGroup);

            GroupBox confirmGroup = new GroupBox()
            {
                Text = "Confirmation",
                Left = 10,
                Top = 260,
                Width = 450,
                Height = 75
            };

            CheckBox chkConfirm = new CheckBox()
            {
                Text = "Ask for confirmation before starting a new scan",
                Left = 15,
                Top = 20,
                Width = 420,
                Height = 20,
                Checked = askConfirmation
            };
            confirmGroup.Controls.Add(chkConfirm);

            CheckBox chkShowInfo = new CheckBox()
            {
                Text = "Show info dialog after each scan",
                Left = 15,
                Top = 45,
                Width = 420,
                Height = 20,
                Checked = showInfoAfterScan
            };
            confirmGroup.Controls.Add(chkShowInfo);

            displayTab.Controls.Add(confirmGroup);

            tabControl.TabPages.Add(displayTab);

            settingsForm.Controls.Add(tabControl);

            Panel bottomPanel = new Panel()
            {
                Left = 0,
                Top = 390,
                Width = 520,
                Height = 60,
                BackColor = Color.FromArgb(240, 240, 240)
            };

            Button btnOK = new Button()
            {
                Text = "OK",
                Left = 330,
                Top = 15,
                Width = 80,
                Height = 30
            };
            btnOK.Click += (s, ev) =>
            {
                threadDelay = (int)numDelay.Value;
                maxThreads = (int)numMaxThreads.Value;
                pingProbes = (int)numProbes.Value;
                pingTimeout = (int)numPingTimeout.Value;
                scanDeadHosts = chkScanDead.Checked;
                skipUnassigned = chkSkipUnassigned.Checked;

                portTimeout = (int)numPortTimeout.Value;
                adaptTimeout = chkAdaptTimeout.Checked;
                minAdaptedTimeout = (int)numMinTimeout.Value;
                customPorts = txtPorts.Text;

                if (rbShowAll.Checked)
                {
                    showDeadHosts = true;
                    showAliveOnly = false;
                    showWithPortsOnly = false;
                }
                else if (rbAliveOnly.Checked)
                {
                    showDeadHosts = false;
                    showAliveOnly = true;
                    showWithPortsOnly = false;
                }
                else if (rbPortsOnly.Checked)
                {
                    showDeadHosts = false;
                    showAliveOnly = false;
                    showWithPortsOnly = true;
                }

                askConfirmation = chkConfirm.Checked;
                showInfoAfterScan = chkShowInfo.Checked;

                settingsForm.DialogResult = DialogResult.OK;
                settingsForm.Close();
            };
            bottomPanel.Controls.Add(btnOK);

            Button btnCancel = new Button()
            {
                Text = "Cancel",
                Left = 420,
                Top = 15,
                Width = 80,
                Height = 30
            };
            btnCancel.Click += (s, ev) => settingsForm.Close();
            bottomPanel.Controls.Add(btnCancel);

            settingsForm.Controls.Add(bottomPanel);
            settingsForm.AcceptButton = btnOK;
            settingsForm.CancelButton = btnCancel;

            settingsForm.ShowDialog(this);
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.ToString();
                        if (!ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254."))
                        {
                            return ipStr;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void SetupDataGridView()
        {
            dgvResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvResults.ReadOnly = true;
            dgvResults.AllowUserToAddRows = false;
            dgvResults.RowTemplate.Height = 25;
            dgvResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvResults.MultiSelect = false;

            DataGridViewImageColumn imgColumn = new DataGridViewImageColumn();
            imgColumn.Name = "Status";
            imgColumn.HeaderText = "Status";
            imgColumn.Width = 60;
            dgvResults.Columns.Add(imgColumn);

            dgvResults.Columns.Add("IP", "IP Address");
            dgvResults.Columns.Add("Ping", "Ping (ms)");
            dgvResults.Columns.Add("Hostname", "Hostname");
            dgvResults.Columns.Add("Ports", "Open Ports");

            dgvResults.MouseClick += DgvResults_MouseClick;
        }

        private void SetupContextMenu()
        {
            contextMenu = new ContextMenuStrip();

            var httpItem = new ToolStripMenuItem("🌐 Open in Browser (HTTP)");
            httpItem.Click += (s, e) => OpenInBrowser("http");
            contextMenu.Items.Add(httpItem);

            var httpsItem = new ToolStripMenuItem("🔒 Open in Browser (HTTPS)");
            httpsItem.Click += (s, e) => OpenInBrowser("https");
            contextMenu.Items.Add(httpsItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var sharesItem = new ToolStripMenuItem("📁 Open Windows File Shares");
            sharesItem.Click += (s, e) => OpenWindowsShares();
            contextMenu.Items.Add(sharesItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var rdpItem = new ToolStripMenuItem("🖥️ Remote Desktop (RDP)");
            rdpItem.Click += (s, e) => OpenRDP();
            contextMenu.Items.Add(rdpItem);

            var sshItem = new ToolStripMenuItem("⌨️ SSH Connection");
            sshItem.Click += (s, e) => OpenSSH();
            contextMenu.Items.Add(sshItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var pingItem = new ToolStripMenuItem("📶 Ping Host (CMD)");
            pingItem.Click += (s, e) => PingHostCMD();
            contextMenu.Items.Add(pingItem);

            var traceItem = new ToolStripMenuItem("🗺️ Traceroute (CMD)");
            traceItem.Click += (s, e) => TracerouteHost();
            contextMenu.Items.Add(traceItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var copyItem = new ToolStripMenuItem("📋 Copy IP Address");
            copyItem.Click += (s, e) => CopyIPAddress();
            contextMenu.Items.Add(copyItem);

            var copyHostItem = new ToolStripMenuItem("📋 Copy Hostname");
            copyHostItem.Click += (s, e) => CopyHostname();
            contextMenu.Items.Add(copyHostItem);

            contextMenu.Opening += ContextMenu_Opening;
        }

        private void ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (dgvResults.SelectedRows.Count == 0 && selectedHostForContextMenu == null)
            {
                e.Cancel = true;
                return;
            }

            string portsStr = "";

            if (selectedHostForContextMenu != null)
            {
                portsStr = selectedHostForContextMenu.openPorts ?? "";
            }
            else if (dgvResults.SelectedRows.Count > 0)
            {
                var selectedRow = dgvResults.SelectedRows[0];
                portsStr = selectedRow.Cells["Ports"].Value?.ToString() ?? "";
            }

            var ports = portsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => int.TryParse(p.Trim(), out int port) ? port : 0)
                               .Where(p => p > 0)
                               .ToList();

            contextMenu.Items[0].Enabled = ports.Contains(80) || ports.Contains(8080);
            contextMenu.Items[1].Enabled = ports.Contains(443);
            contextMenu.Items[3].Enabled = ports.Contains(445) || ports.Contains(139);
            contextMenu.Items[5].Enabled = ports.Contains(3389);
            contextMenu.Items[6].Enabled = ports.Contains(22);
        }

        private void DgvResults_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var hitTest = dgvResults.HitTest(e.X, e.Y);
                if (hitTest.RowIndex >= 0)
                {
                    dgvResults.ClearSelection();
                    dgvResults.Rows[hitTest.RowIndex].Selected = true;
                    contextMenu.Show(dgvResults, e.Location);
                }
            }
        }

        private string GetSelectedIP()
        {
            if (selectedHostForContextMenu != null)
            {
                string ip = selectedHostForContextMenu.ip;
                selectedHostForContextMenu = null;
                return ip;
            }

            if (dgvResults.SelectedRows.Count > 0)
            {
                return dgvResults.SelectedRows[0].Cells["IP"].Value?.ToString() ?? "";
            }
            return "";
        }

        private string GetSelectedHostname()
        {
            if (selectedHostForContextMenu != null)
            {
                string hostname = selectedHostForContextMenu.hostname;
                return hostname;
            }

            if (dgvResults.SelectedRows.Count > 0)
            {
                return dgvResults.SelectedRows[0].Cells["Hostname"].Value?.ToString() ?? "N/A";
            }
            return "N/A";
        }

        private void OpenInBrowser(string protocol)
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                string url = $"{protocol}://{ip}";
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenWindowsShares()
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                string path = $"\\\\{ip}";
                try
                {
                    Process.Start("explorer.exe", path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Windows shares: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenRDP()
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                try
                {
                    Process.Start("mstsc.exe", $"/v:{ip}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Remote Desktop: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenSSH()
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                try
                {
                    string username = PromptForInput("SSH Connection", $"Enter username for {ip}:", "root");

                    if (!string.IsNullOrEmpty(username))
                    {
                        string sshPath = GetSSHPath();

                        if (sshPath != null)
                        {
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/K \"{sshPath}\" {username}@{ip}",
                                UseShellExecute = true
                            };
                            Process.Start(psi);
                        }
                        else
                        {
                            MessageBox.Show("Could not find ssh.exe!", "SSH Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open SSH: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private string GetSSHPath()
        {
            string[] paths =
            {
                @"C:\Windows\System32\OpenSSH\ssh.exe",
                @"C:\Program Files\OpenSSH\ssh.exe",
                @"C:\Program Files\Git\usr\bin\ssh.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe")
            };

            foreach (string path in paths)
            {
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            if (pathEnv != null)
            {
                foreach (string dir in pathEnv.Split(';'))
                {
                    string testPath = System.IO.Path.Combine(dir, "ssh.exe");
                    if (System.IO.File.Exists(testPath))
                    {
                        return testPath;
                    }
                }
            }

            return null;
        }

        private string PromptForInput(string title, string promptText, string defaultValue = "")
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            Label textLabel = new Label() { Left = 20, Top = 20, Width = 350, Text = promptText };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 340, Text = defaultValue };
            Button confirmation = new Button() { Text = "Connect", Left = 220, Width = 80, Top = 80, DialogResult = DialogResult.OK };
            Button cancel = new Button() { Text = "Cancel", Left = 310, Width = 60, Top = 80, DialogResult = DialogResult.Cancel };

            confirmation.Click += (sender, e) => { prompt.Close(); };
            cancel.Click += (sender, e) => { prompt.Close(); };

            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(cancel);
            prompt.AcceptButton = confirmation;
            prompt.CancelButton = cancel;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }

        private void PingHostCMD()
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                try
                {
                    Process.Start("cmd.exe", $"/k ping {ip} -t");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to ping: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void TracerouteHost()
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                try
                {
                    Process.Start("cmd.exe", $"/k tracert {ip}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to traceroute: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CopyIPAddress()
        {
            string ip = GetSelectedIP();
            if (!string.IsNullOrEmpty(ip))
            {
                Clipboard.SetText(ip);
                MessageBox.Show($"IP Address copied: {ip}", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void CopyHostname()
        {
            string hostname = GetSelectedHostname();
            if (hostname != "N/A")
            {
                Clipboard.SetText(hostname);
                MessageBox.Show($"Hostname copied: {hostname}", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool ShouldDisplayHost(ScanResult result)
        {
            if (showWithPortsOnly)
            {
                return result.online && result.portStatus == PortStatus.Open;
            }

            if (showAliveOnly)
            {
                return result.online;
            }

            if (!showDeadHosts && !result.online)
            {
                return false;
            }

            return true;
        }

        private async void BtnScan_Click(object sender, EventArgs e)
        {
            if (askConfirmation)
            {
                var result = MessageBox.Show("Start network scan?", "Confirm Scan", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                    return;
            }

            dgvResults.Rows.Clear();
            scanResults = new ConcurrentBag<ScanResult>();
            btnScan.Enabled = false;
            mapRefreshTimer?.Start();

            string startIP = txtStartIP.Text.Trim();
            string endIP = txtEndIP.Text.Trim();

            string[] startOctets = startIP.Split('.');
            string[] endOctets = endIP.Split('.');

            if (startOctets.Length != 4 || endOctets.Length != 4)
            {
                MessageBox.Show("Please enter valid IP addresses.");
                btnScan.Enabled = true;
                return;
            }

            int start = int.Parse(startOctets[3]);
            int end = int.Parse(endOctets[3]);

            string baseIP = $"{startOctets[0]}.{startOctets[1]}.{startOctets[2]}.";

            progressBar.Minimum = 0;
            progressBar.Maximum = end - start + 1;
            progressBar.Value = 0;

            var startTime = DateTime.Now;

            var semaphore = new System.Threading.SemaphoreSlim(maxThreads);
            var tasks = new List<Task>();
            int scanned = 0;

            for (int i = start; i <= end; i++)
            {
                if (skipUnassigned && (i == 0 || i == 255))
                {
                    System.Threading.Interlocked.Increment(ref scanned);
                    this.Invoke((MethodInvoker)delegate
                    {
                        progressBar.Value = scanned;
                    });
                    continue;
                }

                string ip = baseIP + i;

                if (threadDelay > 0)
                    await Task.Delay(threadDelay);

                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var result = await ScanHostFast(ip);
                        scanResults.Add(result);

                        if (ShouldDisplayHost(result))
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                Bitmap statusIcon = CreateStatusIcon(result.portStatus);

                                if (result.online)
                                {
                                    dgvResults.Rows.Add(statusIcon, result.ip, result.ping, result.hostname, result.openPorts);
                                }
                                else
                                {
                                    dgvResults.Rows.Add(statusIcon, result.ip, "-", "N/A", "Dead");
                                }
                            });
                        }

                        System.Threading.Interlocked.Increment(ref scanned);
                        this.Invoke((MethodInvoker)delegate
                        {
                            progressBar.Value = scanned;
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            var duration = (DateTime.Now - startTime).TotalSeconds;
            btnScan.Enabled = true;

            if (showInfoAfterScan)
            {
                int totalScanned = scanResults.Count;
                int onlineCount = scanResults.Count(r => r.online);
                int offlineCount = scanResults.Count(r => !r.online);
                int withPortsCount = scanResults.Count(r => r.online && r.portStatus == PortStatus.Open);
                int displayed = dgvResults.Rows.Count;

                MessageBox.Show(
                    $"Scan completed in {duration:F1}s\n\n" +
                    $"Total scanned: {totalScanned}\n" +
                    $"Alive: {onlineCount} | Dead: {offlineCount}\n" +
                    $"With open ports: {withPortsCount}\n\n" +
                    $"Displayed: {displayed} hosts",
                    "Scan Complete"
                );
            }

            mapRefreshTimer?.Stop();
            networkMapPanel?.Invalidate();
        }

        private async Task<ScanResult> ScanHostFast(string ip)
        {
            try
            {
                using (Ping pinger = new Ping())
                {
                    PingReply reply = null;

                    for (int attempt = 0; attempt < pingProbes; attempt++)
                    {
                        try
                        {
                            reply = await pinger.SendPingAsync(ip, pingTimeout);
                            if (reply.Status == IPStatus.Success)
                                break;
                        }
                        catch { }
                    }

                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        var hostnameTask = GetHostnameAsync(ip);
                        var portsTask = ScanPortsFromSettings(ip);

                        await Task.WhenAll(hostnameTask, portsTask);

                        string hostname = await hostnameTask;
                        var openPorts = await portsTask;

                        PortStatus status;
                        string portsString;

                        if (openPorts.Count > 0)
                        {
                            status = PortStatus.Open;
                            portsString = string.Join(", ", openPorts);
                        }
                        else
                        {
                            status = PortStatus.Closed;
                            portsString = "None";
                        }

                        return new ScanResult
                        {
                            online = true,
                            ip = ip,
                            ping = reply.RoundtripTime,
                            hostname = hostname,
                            portStatus = status,
                            openPorts = portsString
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning {ip}: {ex.Message}");
            }

            return new ScanResult
            {
                online = false,
                ip = ip,
                portStatus = PortStatus.Dead
            };
        }

        private async Task<string> GetHostnameAsync(string ip)
        {
            try
            {
                var hostTask = System.Net.Dns.GetHostEntryAsync(ip);
                if (await Task.WhenAny(hostTask, Task.Delay(2000)) == hostTask)
                {
                    return hostTask.Result.HostName;
                }
            }
            catch { }
            return "N/A";
        }

        private async Task<List<int>> ScanPortsFromSettings(string ip)
        {
            var openPorts = new List<int>();

            List<int> portsToScan = new List<int>();
            try
            {
                string[] parts = customPorts.Split(',');
                foreach (string part in parts)
                {
                    string trimmed = part.Trim();
                    if (trimmed.Contains("-"))
                    {
                        string[] range = trimmed.Split('-');
                        int rangeStart = int.Parse(range[0].Trim());
                        int rangeEnd = int.Parse(range[1].Trim());
                        for (int p = rangeStart; p <= rangeEnd; p++)
                        {
                            portsToScan.Add(p);
                        }
                    }
                    else
                    {
                        portsToScan.Add(int.Parse(trimmed));
                    }
                }
            }
            catch
            {
                portsToScan = new List<int> { 21, 22, 23, 80, 443, 445, 3389, 5900, 8080 };
            }

            var portTasks = portsToScan.Select(port => CheckPortFast(ip, port)).ToArray();
            var results = await Task.WhenAll(portTasks);

            for (int i = 0; i < portsToScan.Count; i++)
            {
                if (results[i])
                {
                    openPorts.Add(portsToScan[i]);
                }
            }

            return openPorts;
        }

        private async Task<bool> CheckPortFast(string ip, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    int timeout = portTimeout;

                    if (adaptTimeout)
                    {
                        timeout = Math.Max(minAdaptedTimeout, portTimeout);
                    }

                    var connectTask = client.ConnectAsync(ip, port);
                    var timeoutTask = Task.Delay(timeout);

                    if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                    {
                        return client.Connected;
                    }
                }
            }
            catch { }
            return false;
        }

        private Bitmap CreateStatusIcon(PortStatus status)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                Color color;
                switch (status)
                {
                    case PortStatus.Open:
                        color = Color.FromArgb(40, 167, 69);
                        break;
                    case PortStatus.Closed:
                        color = Color.FromArgb(0, 120, 215);
                        break;
                    case PortStatus.Dead:
                        color = Color.FromArgb(220, 53, 69);
                        break;
                    default:
                        color = Color.Gray;
                        break;
                }

                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, 2, 2, 12, 12);
                }

                using (Pen pen = new Pen(Color.Black, 1))
                {
                    g.DrawEllipse(pen, 2, 2, 12, 12);
                }
            }
            return bmp;
        }

        private void txtStartIP_TextChanged(object sender, EventArgs e)
        {

        }

        private void txtEndIP_TextChanged(object sender, EventArgs e)
        {

        }

        private enum PortStatus
        {
            Dead,
            Closed,
            Open
        }

        private class ScanResult
        {
            public bool online;
            public string ip;
            public long ping;
            public string hostname;
            public PortStatus portStatus;
            public string openPorts;
        }
    }

    public class CustomProgressBar : ProgressBar
    {
        public CustomProgressBar()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle rec = new Rectangle(0, 0, this.Width, this.Height);

            if (ProgressBarRenderer.IsSupported)
            {
                ProgressBarRenderer.DrawHorizontalBar(e.Graphics, rec);
            }

            rec.Inflate(-3, -3);
            if (Maximum > 0 && Value > 0)
            {
                Rectangle clip = new Rectangle(rec.X, rec.Y, (int)Math.Round(((float)Value / Maximum) * rec.Width), rec.Height);

                using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 120, 215)))
                {
                    e.Graphics.FillRectangle(brush, clip);
                }
            }
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
        }
    }
}