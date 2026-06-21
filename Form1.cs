#nullable disable

using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ConveyorApp
{
    public partial class Form1 : Form
    {
        // UI Controls
        private PictureBox pictureBox1;
        private TextBox txtLog, txtServerPath, txtSyncInterval;
        private ComboBox cmbTriggerPort, cmbConveyorPort;
        private Button btnSetRoi, btnStart;

        // Logic Variables
        private VideoCapture camera;
        private QRCodeDetector qrDecoder;
        private Rect roiRect = new Rect(0, 0, 0, 0); 
        private bool isRunning = false;
        private SerialPort triggerPort, conveyorPort;
        private System.Windows.Forms.Timer videoTimer;

        public Form1()
        {
            BuildUI(); 
            qrDecoder = new QRCodeDetector();
            LoadPorts();
            StartCamera();
            StartSyncTask();
        }

        private void BuildUI()
        {
            this.Text = "Industrial QR Conveyor Control";
            this.Size = new System.Drawing.Size(1000, 600); 
            this.FormClosing += Form1_FormClosing;

            pictureBox1 = new PictureBox { 
                Location = new System.Drawing.Point(10, 10), 
                Size = new System.Drawing.Size(640, 480), 
                BackColor = Color.Black, 
                SizeMode = PictureBoxSizeMode.StretchImage 
            };
            this.Controls.Add(pictureBox1);

            int rightX = 670;
            Label lbl1 = new Label { Text = "Trigger Port:", Location = new System.Drawing.Point(rightX, 20) };
            cmbTriggerPort = new ComboBox { Location = new System.Drawing.Point(rightX, 40), Width = 150 };
            
            Label lbl2 = new Label { Text = "Conveyor Port:", Location = new System.Drawing.Point(rightX, 80) };
            cmbConveyorPort = new ComboBox { Location = new System.Drawing.Point(rightX, 100), Width = 150 };

            Label lbl3 = new Label { Text = "Server Path:", Location = new System.Drawing.Point(rightX, 140) };
            txtServerPath = new TextBox { Location = new System.Drawing.Point(rightX, 160), Width = 200, Text = @"C:\ServerBackup" };

            Label lbl4 = new Label { Text = "Sync Interval (Min):", Location = new System.Drawing.Point(rightX, 200) };
            txtSyncInterval = new TextBox { Location = new System.Drawing.Point(rightX, 220), Width = 100, Text = "5" };

            btnSetRoi = new Button { Text = "1. Set ROI", Location = new System.Drawing.Point(rightX, 270), Width = 200, Height = 40, BackColor = Color.LightBlue };
            btnSetRoi.Click += BtnSetRoi_Click;

            btnStart = new Button { Text = "2. Start System", Location = new System.Drawing.Point(rightX, 320), Width = 200, Height = 40, BackColor = Color.LightGreen };
            btnStart.Click += BtnStart_Click;

            txtLog = new TextBox { Location = new System.Drawing.Point(rightX, 380), Width = 280, Height = 150, Multiline = true, ScrollBars = ScrollBars.Vertical };

            this.Controls.AddRange(new Control[] { lbl1, cmbTriggerPort, lbl2, cmbConveyorPort, lbl3, txtServerPath, lbl4, txtSyncInterval, btnSetRoi, btnStart, txtLog });
        }

        private void LoadPorts()
        {
            try
            {
                string[] ports = SerialPort.GetPortNames();
                cmbTriggerPort.Items.AddRange(ports);
                cmbConveyorPort.Items.AddRange(ports);
            }
            catch { }
        }

        private void LogMsg(string msg)
        {
            if (txtLog.InvokeRequired) { txtLog.Invoke(new Action(() => LogMsg(msg))); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            txtLog.ScrollToCaret();
        }

        private void StartCamera()
        {
            camera = new VideoCapture(0);
            videoTimer = new System.Windows.Forms.Timer { Interval = 30 };
            videoTimer.Tick += (s, e) =>
            {
                using (Mat frame = new Mat())
                {
                    if (camera.Read(frame))
                    {
                        if (roiRect.Width > 0 && roiRect.Height > 0) 
                        {
                            Cv2.Rectangle(frame, roiRect, Scalar.LimeGreen, 2);
                        }
                        var oldImage = pictureBox1.Image;
                        pictureBox1.Image = BitmapConverter.ToBitmap(frame);
                        if (oldImage != null) oldImage.Dispose();
                    }
                }
            };
            videoTimer.Start();
        }

        private void BtnSetRoi_Click(object sender, EventArgs e)
        {
            using (Mat frame = new Mat())
            {
                if (camera.Read(frame))
                {
                    MessageBox.Show("Draw ROI and press SPACE or ENTER to confirm.");
                    roiRect = Cv2.SelectROI("Select QR ROI", frame, false, true);
                    Cv2.DestroyWindow("Select QR ROI");
                    LogMsg("ROI Set.");
                }
            }
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (!isRunning)
            {
                if (roiRect.Width == 0 || roiRect.Height == 0) { MessageBox.Show("Set ROI first."); return; }
                try
                {
                    triggerPort = new SerialPort(cmbTriggerPort.SelectedItem?.ToString() ?? "", 9600);
                    triggerPort.DataReceived += TriggerPort_DataReceived;
                    triggerPort.Open();

                    conveyorPort = new SerialPort(cmbConveyorPort.SelectedItem?.ToString() ?? "", 9600);
                    conveyorPort.Open();

                    isRunning = true;
                    btnStart.Text = "Stop System";
                    btnStart.BackColor = Color.Salmon;
                    LogMsg("System Armed.");
                }
                catch (Exception ex) { MessageBox.Show("Port Error: " + ex.Message); }
            }
            else
            {
                isRunning = false;
                if (triggerPort != null && triggerPort.IsOpen) triggerPort.Close();
                if (conveyorPort != null && conveyorPort.IsOpen) conveyorPort.Close();
                btnStart.Text = "Start System";
                btnStart.BackColor = Color.LightGreen;
                LogMsg("System Stopped.");
            }
        }

        private void TriggerPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!isRunning) return;
            string data = triggerPort.ReadLine().Trim();
            if (data == "1")
            {
                for (int i = 0; i < 5; i++) camera.Grab();
                using (Mat frame = new Mat())
                {
                    if (camera.Read(frame)) ProcessAndSave(frame);
                }
            }
        }

        private void ProcessAndSave(Mat frame)
        {
            using (Mat roiCrop = new Mat(frame, roiRect))
            using (Mat straightQrCode = new Mat()) 
            {
                string decodedInfo = qrDecoder.DetectAndDecode(roiCrop, out var points, straightQrCode);
                
                if (!string.IsNullOrEmpty(decodedInfo))
                {
                    string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                    string localDir = Path.Combine(Environment.CurrentDirectory, "Local_Data", dateFolder);
                    Directory.CreateDirectory(localDir);

                    string safeName = new string(decodedInfo.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray());
                    Cv2.ImWrite(Path.Combine(localDir, $"{safeName}.jpg"), frame);
                    LogMsg($"Saved: {safeName}.jpg");

                    if (conveyorPort != null && conveyorPort.IsOpen) conveyorPort.Write("1");
                }
                else 
                { 
                    LogMsg("QR Not Found."); 
                }
            }
        }

        private void StartSyncTask()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    int interval = 5;
                    if (txtSyncInterval.InvokeRequired) txtSyncInterval.Invoke(new Action(() => int.TryParse(txtSyncInterval.Text, out interval)));
                    await Task.Delay(interval * 60 * 1000);
                    try
                    {
                        string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                        string localDir = Path.Combine(Environment.CurrentDirectory, "Local_Data", dateFolder);
                        string serverDir = Path.Combine(txtServerPath.Text, dateFolder);

                        if (Directory.Exists(localDir))
                        {
                            Directory.CreateDirectory(serverDir);
                            foreach (var file in Directory.GetFiles(localDir))
                            {
                                string destFile = Path.Combine(serverDir, Path.GetFileName(file));
                                if (!File.Exists(destFile)) File.Copy(file, destFile);
                            }
                            LogMsg("Sync Complete.");
                        }
                    }
                    catch { }
                }
            });
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isRunning) BtnStart_Click(null, null);
            if (camera != null) camera.Release();
        }
    }
}