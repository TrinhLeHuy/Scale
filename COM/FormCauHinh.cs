using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Windows.Forms;

namespace CanDienTu
{
    public partial class FormCauHinh : Form
    {
        private SerialConnection _connection;
        private FormDebug _debugForm;
        private readonly string _configPath =
            Path.Combine(Application.StartupPath, "cauhinh.ini");

        public FormCauHinh()
        {
            InitializeComponent();
            Load += FormCauHinh_Load;
        }

        private void FormCauHinh_Load(object sender, EventArgs e)
        {
            NapDanhSachCong();
            NapChiSoMacDinh();
            NapBaudRate();
            DocCauHinh();

            if (SerialConnection.GetAvailablePorts().Length == 0)
                lblTrangThai.Text = "Khong tim thay cong COM nao tren may.";
            else
                lblTrangThai.Text = "Chua ket noi. Chon cong 'Xe vao' roi bam Ket noi.";
        }

        private void NapBaudRate()
        {
            cboBaud.Items.Clear();
            cboBaud.Items.AddRange(new object[]
            {
                "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200"
            });
            cboBaud.SelectedItem = "9600";
        }

        /// <summary>
        /// Nap danh sach cong COM thuc te vao tat ca combobox chon cong.
        /// </summary>
        private void NapDanhSachCong()
        {
            string[] ports = SerialConnection.GetAvailablePorts();
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);

            ComboBox[] comboCong =
            {
                cboBx, cboPol, cboCanRacCom, cboXeVao, cboXeRa,
                cboCongBaoVe, cboTramCan, cboTramCau,
                cboBaoVeQuet, cboTramCanQuet, cboCongCan
            };

            foreach (ComboBox cb in comboCong)
            {
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                cb.Items.Clear();
                cb.Items.Add("(khong)");
                cb.Items.AddRange(ports);
                cb.SelectedIndex = 0;
            }

            // Mac dinh chon cong cau can: uu tien COM2 (noi dau can gui du lieu),
            // neu khong co thi lay cong dau tien.
            if (cboCongCan.Items.Contains("COM2"))
                cboCongCan.SelectedItem = "COM2";
            else if (ports.Length > 0)
                cboCongCan.SelectedItem = ports[0];
        }

        private void NapChiSoMacDinh()
        {
            cboChiSoF.DropDownStyle = ComboBoxStyle.DropDown;
            cboChiSoF.Items.Clear();
            cboChiSoF.Items.AddRange(new object[] { "21.27", "20.00", "22.50" });
            cboChiSoF.SelectedIndex = 0;

            cboHeSoK.DropDownStyle = ComboBoxStyle.DropDown;
            cboHeSoK.Items.Clear();
            cboHeSoK.Items.AddRange(new object[] { "1", "2", "3" });
            cboHeSoK.SelectedIndex = 0;
        }

        private void btnTest_Click(object sender, EventArgs e)
        {
            // Dang ket noi -> ngat.
            if (_connection != null && _connection.IsOpen)
            {
                NgatKetNoi();
                lblTrangThai.Text = "Da ngat ket noi.";
                btnTest.Text = "Ket noi can";
                return;
            }

            string port = LayCong(cboCongCan);
            if (port == null)
            {
                MessageBox.Show(
                    "Vui long chon 'Cong cau can' (goc trai duoi) truoc khi ket noi.",
                    "Chua chon cong COM",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int baud = 9600;
            if (cboBaud.SelectedItem != null)
                int.TryParse(cboBaud.SelectedItem.ToString(), out baud);

            try
            {
                _connection = new SerialConnection();
                _connection.WeightReceived += OnWeightReceived;
                _connection.ConnectionError += OnConnectionError;
                _connection.RawBytesReceived += OnRawBytes;
                // Cau hinh pho bien cho dau can: 8 data bits, khong parity, 1 stop bit.
                _connection.Open(port, baud, 8, Parity.None, StopBits.One);

                lblTrangThai.Text = "Da ket noi " + port + " @" + baud + ", dang cho du lieu...";
                btnTest.Text = "Ngat ket noi";

                // Tu dong luu lua chon ngay khi ket noi thanh cong.
                try { LuuCauHinh(); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Khong mo duoc cong " + port + ":\r\n" + ex.Message +
                    "\r\n\r\nKiem tra: cong co bi phan mem khac chiem khong, " +
                    "day co cam dung khong, baud rate co dung voi dau can khong.",
                    "Loi ket noi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblTrangThai.Text = "Loi ket noi.";
            }
        }

        private void NgatKetNoi()
        {
            if (_connection != null)
            {
                _connection.Close();
                _connection.Dispose();
                _connection = null;
            }
        }

        private void OnWeightReceived(object sender, WeightEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler<WeightEventArgs>(OnWeightReceived), sender, e);
                return;
            }
            lblTrongLuong.Text = string.Format(
                CultureInfo.InvariantCulture, "{0:0.##} kg", e.Weight);
            lblTrangThai.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Trong luong: {0:0.##} kg (raw: {1})", e.Weight, e.Raw);
        }

        private void OnConnectionError(object sender, Exception e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler<Exception>(OnConnectionError), sender, e);
                return;
            }
            lblTrangThai.Text = "Loi: " + e.Message;
        }

        private void OnRawBytes(object sender, byte[] bytes)
        {
            if (_debugForm != null && !_debugForm.IsDisposed)
                _debugForm.AppendBytes(bytes);
        }

        private void btnDebug_Click(object sender, EventArgs e)
        {
            if (_debugForm == null || _debugForm.IsDisposed)
                _debugForm = new FormDebug();
            _debugForm.Show();
            _debugForm.BringToFront();
        }

        private void btnAutoDetect_Click(object sender, EventArgs e)
        {
            // Dang ket noi thi ngat truoc khi do.
            if (_connection != null && _connection.IsOpen)
            {
                NgatKetNoi();
                btnTest.Text = "Ket noi can";
            }

            int baud = 9600;
            if (cboBaud.SelectedItem != null)
                int.TryParse(cboBaud.SelectedItem.ToString(), out baud);

            Cursor = Cursors.WaitCursor;
            lblTrangThai.Text = "Dang do tim cong co du lieu can...";
            Application.DoEvents();

            string chiTiet;
            string cong = SerialConnection.DoTimCong(baud, out chiTiet);

            Cursor = Cursors.Default;

            if (cong == null)
            {
                MessageBox.Show(
                    "Khong tim thay cong nao dang nhan du lieu @ " + baud + " baud.\r\n\r\n" +
                    "Chi tiet:\r\n" + chiTiet +
                    "\r\nKiem tra: dau can da bat chua, day co cam dung khong, " +
                    "thu doi baud rate roi do lai.",
                    "Khong tim thay cong can",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                lblTrangThai.Text = "Khong tim thay cong co du lieu.";
                return;
            }

            // Tim thay -> chon cong do va ket noi luon.
            if (cboCongCan.Items.Contains(cong))
                cboCongCan.SelectedItem = cong;

            lblTrangThai.Text = "Tim thay du lieu o " + cong + ". Dang ket noi...";
            btnTest_Click(sender, e); // ket noi theo cboCongCan vua chon
        }

        private static string LayCong(ComboBox cb)
        {
            if (cb.SelectedItem == null)
                return null;
            string value = cb.SelectedItem.ToString();
            return value == "(khong)" ? null : value;
        }

        private void btnGhi_Click(object sender, EventArgs e)
        {
            try
            {
                LuuCauHinh();
                MessageBox.Show("Da luu cau hinh.", "Thong bao",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Khong luu duoc cau hinh:\r\n" + ex.Message,
                    "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnBoQua_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void LuuCauHinh()
        {
            var lines = new List<string>
            {
                "ChiSoF=" + cboChiSoF.Text,
                "HeSoK=" + cboHeSoK.Text,
                "VaoMin=" + numVaoMin.Value.ToString(CultureInfo.InvariantCulture),
                "VaoMax=" + numVaoMax.Value.ToString(CultureInfo.InvariantCulture),
                "RaMin=" + numRaMin.Value.ToString(CultureInfo.InvariantCulture),
                "RaMax=" + numRaMax.Value.ToString(CultureInfo.InvariantCulture),
                "Bx=" + TextCong(cboBx),
                "Pol=" + TextCong(cboPol),
                "CanRacCom=" + TextCong(cboCanRacCom),
                "Baud=" + (cboBaud.SelectedItem != null ? cboBaud.SelectedItem.ToString() : "9600"),
                "CongCan=" + TextCong(cboCongCan),
                "XeVao=" + TextCong(cboXeVao),
                "HeSoXeVao=" + numHeSoXeVao.Value.ToString(CultureInfo.InvariantCulture),
                "XeRa=" + TextCong(cboXeRa),
                "HeSoXeRa=" + numHeSoXeRa.Value.ToString(CultureInfo.InvariantCulture),
                "CanXeVao=" + chkCanXeVao.Checked,
                "CanXeRa=" + chkCanXeRa.Checked,
                "DoCcs=" + chkDoCcs.Checked,
                "CanRac=" + chkCanRac.Checked,
                "CongBaoVe=" + TextCong(cboCongBaoVe),
                "TramCan=" + TextCong(cboTramCan),
                "TramCau=" + TextCong(cboTramCau),
                "BaoVeQuet=" + TextCong(cboBaoVeQuet),
                "TramCanQuet=" + TextCong(cboTramCanQuet)
            };
            File.WriteAllLines(_configPath, lines);
        }

        private static string TextCong(ComboBox cb)
        {
            return cb.SelectedItem != null ? cb.SelectedItem.ToString() : "";
        }

        private void DocCauHinh()
        {
            if (!File.Exists(_configPath))
                return;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(_configPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq)] = line.Substring(eq + 1);
            }

            SetText(cboChiSoF, map, "ChiSoF");
            SetText(cboHeSoK, map, "HeSoK");
            SetNum(numVaoMin, map, "VaoMin");
            SetNum(numVaoMax, map, "VaoMax");
            SetNum(numRaMin, map, "RaMin");
            SetNum(numRaMax, map, "RaMax");
            string baud;
            if (map.TryGetValue("Baud", out baud) && cboBaud.Items.Contains(baud))
                cboBaud.SelectedItem = baud;
            SetCong(cboCongCan, map, "CongCan");
            SetCong(cboBx, map, "Bx");
            SetCong(cboPol, map, "Pol");
            SetCong(cboCanRacCom, map, "CanRacCom");
            SetCong(cboXeVao, map, "XeVao");
            SetNum(numHeSoXeVao, map, "HeSoXeVao");
            SetCong(cboXeRa, map, "XeRa");
            SetNum(numHeSoXeRa, map, "HeSoXeRa");
            SetCheck(chkCanXeVao, map, "CanXeVao");
            SetCheck(chkCanXeRa, map, "CanXeRa");
            SetCheck(chkDoCcs, map, "DoCcs");
            SetCheck(chkCanRac, map, "CanRac");
            SetCong(cboCongBaoVe, map, "CongBaoVe");
            SetCong(cboTramCan, map, "TramCan");
            SetCong(cboTramCau, map, "TramCau");
            SetCong(cboBaoVeQuet, map, "BaoVeQuet");
            SetCong(cboTramCanQuet, map, "TramCanQuet");
        }

        private static void SetText(ComboBox cb, Dictionary<string, string> map, string key)
        {
            string v;
            if (map.TryGetValue(key, out v))
                cb.Text = v;
        }

        private static void SetCong(ComboBox cb, Dictionary<string, string> map, string key)
        {
            string v;
            if (map.TryGetValue(key, out v) && cb.Items.Contains(v))
                cb.SelectedItem = v;
        }

        private static void SetNum(NumericUpDown num, Dictionary<string, string> map, string key)
        {
            string v;
            decimal d;
            if (map.TryGetValue(key, out v) &&
                decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
            {
                if (d < num.Minimum) d = num.Minimum;
                if (d > num.Maximum) d = num.Maximum;
                num.Value = d;
            }
        }

        private static void SetCheck(CheckBox chk, Dictionary<string, string> map, string key)
        {
            string v;
            bool b;
            if (map.TryGetValue(key, out v) && bool.TryParse(v, out b))
                chk.Checked = b;
        }
    }
}
