namespace CanDienTu
{
    partial class FormCauHinh
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_connection != null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.grpChiSo = new System.Windows.Forms.GroupBox();
            this.lblChiSoF = new System.Windows.Forms.Label();
            this.cboChiSoF = new System.Windows.Forms.ComboBox();
            this.lblHeSoK = new System.Windows.Forms.Label();
            this.cboHeSoK = new System.Windows.Forms.ComboBox();
            this.lblVaoMin = new System.Windows.Forms.Label();
            this.numVaoMin = new System.Windows.Forms.NumericUpDown();
            this.lblVaoMax = new System.Windows.Forms.Label();
            this.numVaoMax = new System.Windows.Forms.NumericUpDown();
            this.lblRaMin = new System.Windows.Forms.Label();
            this.numRaMin = new System.Windows.Forms.NumericUpDown();
            this.lblRaMax = new System.Windows.Forms.Label();
            this.numRaMax = new System.Windows.Forms.NumericUpDown();

            this.grpCongCom = new System.Windows.Forms.GroupBox();
            this.lblBx = new System.Windows.Forms.Label();
            this.cboBx = new System.Windows.Forms.ComboBox();
            this.lblPol = new System.Windows.Forms.Label();
            this.cboPol = new System.Windows.Forms.ComboBox();
            this.lblCanRac = new System.Windows.Forms.Label();
            this.cboCanRacCom = new System.Windows.Forms.ComboBox();
            this.lblXeVao = new System.Windows.Forms.Label();
            this.cboXeVao = new System.Windows.Forms.ComboBox();
            this.lblHeSoXeVao = new System.Windows.Forms.Label();
            this.numHeSoXeVao = new System.Windows.Forms.NumericUpDown();
            this.lblXeRa = new System.Windows.Forms.Label();
            this.cboXeRa = new System.Windows.Forms.ComboBox();
            this.lblHeSoXeRa = new System.Windows.Forms.Label();
            this.numHeSoXeRa = new System.Windows.Forms.NumericUpDown();

            this.grpTinhTrang = new System.Windows.Forms.GroupBox();
            this.chkCanXeVao = new System.Windows.Forms.CheckBox();
            this.chkCanXeRa = new System.Windows.Forms.CheckBox();
            this.chkDoCcs = new System.Windows.Forms.CheckBox();
            this.chkCanRac = new System.Windows.Forms.CheckBox();

            this.grpQuangBao = new System.Windows.Forms.GroupBox();
            this.lblCongBaoVe = new System.Windows.Forms.Label();
            this.cboCongBaoVe = new System.Windows.Forms.ComboBox();
            this.lblTramCan = new System.Windows.Forms.Label();
            this.cboTramCan = new System.Windows.Forms.ComboBox();
            this.lblTramCau = new System.Windows.Forms.Label();
            this.cboTramCau = new System.Windows.Forms.ComboBox();

            this.grpMayQuet = new System.Windows.Forms.GroupBox();
            this.lblBaoVeQuet = new System.Windows.Forms.Label();
            this.cboBaoVeQuet = new System.Windows.Forms.ComboBox();
            this.lblTramCanQuet = new System.Windows.Forms.Label();
            this.cboTramCanQuet = new System.Windows.Forms.ComboBox();

            this.lblBaud = new System.Windows.Forms.Label();
            this.cboBaud = new System.Windows.Forms.ComboBox();
            this.lblCongCan = new System.Windows.Forms.Label();
            this.cboCongCan = new System.Windows.Forms.ComboBox();
            this.btnDebug = new System.Windows.Forms.Button();
            this.btnAutoDetect = new System.Windows.Forms.Button();
            this.lblTrongLuong = new System.Windows.Forms.Label();

            this.btnGhi = new System.Windows.Forms.Button();
            this.btnBoQua = new System.Windows.Forms.Button();
            this.btnTest = new System.Windows.Forms.Button();
            this.lblTrangThai = new System.Windows.Forms.Label();

            this.grpChiSo.SuspendLayout();
            this.grpCongCom.SuspendLayout();
            this.grpTinhTrang.SuspendLayout();
            this.grpQuangBao.SuspendLayout();
            this.grpMayQuet.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numVaoMin)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numVaoMax)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRaMin)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRaMax)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHeSoXeVao)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHeSoXeRa)).BeginInit();
            this.SuspendLayout();

            // grpChiSo
            this.grpChiSo.Controls.Add(this.lblChiSoF);
            this.grpChiSo.Controls.Add(this.cboChiSoF);
            this.grpChiSo.Controls.Add(this.lblHeSoK);
            this.grpChiSo.Controls.Add(this.cboHeSoK);
            this.grpChiSo.Controls.Add(this.lblVaoMin);
            this.grpChiSo.Controls.Add(this.numVaoMin);
            this.grpChiSo.Controls.Add(this.lblVaoMax);
            this.grpChiSo.Controls.Add(this.numVaoMax);
            this.grpChiSo.Controls.Add(this.lblRaMin);
            this.grpChiSo.Controls.Add(this.numRaMin);
            this.grpChiSo.Controls.Add(this.lblRaMax);
            this.grpChiSo.Controls.Add(this.numRaMax);
            this.grpChiSo.Location = new System.Drawing.Point(12, 12);
            this.grpChiSo.Name = "grpChiSo";
            this.grpChiSo.Size = new System.Drawing.Size(340, 190);
            this.grpChiSo.TabIndex = 0;
            this.grpChiSo.TabStop = false;
            this.grpChiSo.Text = "Cac chi so";

            this.lblChiSoF.AutoSize = true;
            this.lblChiSoF.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblChiSoF.Location = new System.Drawing.Point(15, 30);
            this.lblChiSoF.Name = "lblChiSoF";
            this.lblChiSoF.Text = "Chi so F";

            this.cboChiSoF.Location = new System.Drawing.Point(110, 27);
            this.cboChiSoF.Name = "cboChiSoF";
            this.cboChiSoF.Size = new System.Drawing.Size(210, 24);
            this.cboChiSoF.TabIndex = 1;

            this.lblHeSoK.AutoSize = true;
            this.lblHeSoK.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeSoK.Location = new System.Drawing.Point(15, 68);
            this.lblHeSoK.Name = "lblHeSoK";
            this.lblHeSoK.Text = "He so K";

            this.cboHeSoK.Location = new System.Drawing.Point(110, 65);
            this.cboHeSoK.Name = "cboHeSoK";
            this.cboHeSoK.Size = new System.Drawing.Size(210, 24);
            this.cboHeSoK.TabIndex = 2;

            this.lblVaoMin.AutoSize = true;
            this.lblVaoMin.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblVaoMin.Location = new System.Drawing.Point(15, 108);
            this.lblVaoMin.Name = "lblVaoMin";
            this.lblVaoMin.Text = "Vao min";

            this.numVaoMin.Location = new System.Drawing.Point(90, 105);
            this.numVaoMin.Maximum = new decimal(new int[] { 99999, 0, 0, 0 });
            this.numVaoMin.Name = "numVaoMin";
            this.numVaoMin.Size = new System.Drawing.Size(70, 22);
            this.numVaoMin.TabIndex = 3;
            this.numVaoMin.Value = new decimal(new int[] { 50, 0, 0, 0 });

            this.lblVaoMax.AutoSize = true;
            this.lblVaoMax.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblVaoMax.Location = new System.Drawing.Point(170, 108);
            this.lblVaoMax.Name = "lblVaoMax";
            this.lblVaoMax.Text = "Max";

            this.numVaoMax.Location = new System.Drawing.Point(215, 105);
            this.numVaoMax.Maximum = new decimal(new int[] { 99999, 0, 0, 0 });
            this.numVaoMax.Name = "numVaoMax";
            this.numVaoMax.Size = new System.Drawing.Size(105, 22);
            this.numVaoMax.TabIndex = 4;
            this.numVaoMax.Value = new decimal(new int[] { 99999, 0, 0, 0 });

            this.lblRaMin.AutoSize = true;
            this.lblRaMin.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblRaMin.Location = new System.Drawing.Point(15, 148);
            this.lblRaMin.Name = "lblRaMin";
            this.lblRaMin.Text = "Ra min";

            this.numRaMin.Location = new System.Drawing.Point(90, 145);
            this.numRaMin.Maximum = new decimal(new int[] { 99999, 0, 0, 0 });
            this.numRaMin.Name = "numRaMin";
            this.numRaMin.Size = new System.Drawing.Size(70, 22);
            this.numRaMin.TabIndex = 5;
            this.numRaMin.Value = new decimal(new int[] { 50, 0, 0, 0 });

            this.lblRaMax.AutoSize = true;
            this.lblRaMax.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblRaMax.Location = new System.Drawing.Point(170, 148);
            this.lblRaMax.Name = "lblRaMax";
            this.lblRaMax.Text = "Max";

            this.numRaMax.Location = new System.Drawing.Point(215, 145);
            this.numRaMax.Maximum = new decimal(new int[] { 99999, 0, 0, 0 });
            this.numRaMax.Name = "numRaMax";
            this.numRaMax.Size = new System.Drawing.Size(105, 22);
            this.numRaMax.TabIndex = 6;
            this.numRaMax.Value = new decimal(new int[] { 99999, 0, 0, 0 });

            // grpCongCom
            this.grpCongCom.Controls.Add(this.lblBx);
            this.grpCongCom.Controls.Add(this.cboBx);
            this.grpCongCom.Controls.Add(this.lblPol);
            this.grpCongCom.Controls.Add(this.cboPol);
            this.grpCongCom.Controls.Add(this.lblCanRac);
            this.grpCongCom.Controls.Add(this.cboCanRacCom);
            this.grpCongCom.Controls.Add(this.lblXeVao);
            this.grpCongCom.Controls.Add(this.cboXeVao);
            this.grpCongCom.Controls.Add(this.lblHeSoXeVao);
            this.grpCongCom.Controls.Add(this.numHeSoXeVao);
            this.grpCongCom.Controls.Add(this.lblXeRa);
            this.grpCongCom.Controls.Add(this.cboXeRa);
            this.grpCongCom.Controls.Add(this.lblHeSoXeRa);
            this.grpCongCom.Controls.Add(this.numHeSoXeRa);
            this.grpCongCom.Controls.Add(this.lblBaud);
            this.grpCongCom.Controls.Add(this.cboBaud);
            this.grpCongCom.Location = new System.Drawing.Point(360, 12);
            this.grpCongCom.Name = "grpCongCom";
            this.grpCongCom.Size = new System.Drawing.Size(340, 225);
            this.grpCongCom.TabIndex = 1;
            this.grpCongCom.TabStop = false;
            this.grpCongCom.Text = "Cac cong COM";

            this.lblBx.AutoSize = true;
            this.lblBx.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblBx.Location = new System.Drawing.Point(15, 30);
            this.lblBx.Name = "lblBx";
            this.lblBx.Text = "Bx";

            this.cboBx.Location = new System.Drawing.Point(55, 27);
            this.cboBx.Name = "cboBx";
            this.cboBx.Size = new System.Drawing.Size(90, 24);
            this.cboBx.TabIndex = 1;

            this.lblPol.AutoSize = true;
            this.lblPol.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblPol.Location = new System.Drawing.Point(160, 30);
            this.lblPol.Name = "lblPol";
            this.lblPol.Text = "Pol";

            this.cboPol.Location = new System.Drawing.Point(200, 27);
            this.cboPol.Name = "cboPol";
            this.cboPol.Size = new System.Drawing.Size(120, 24);
            this.cboPol.TabIndex = 2;

            this.lblCanRac.AutoSize = true;
            this.lblCanRac.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblCanRac.Location = new System.Drawing.Point(15, 68);
            this.lblCanRac.Name = "lblCanRac";
            this.lblCanRac.Text = "Can rac";

            this.cboCanRacCom.Location = new System.Drawing.Point(110, 65);
            this.cboCanRacCom.Name = "cboCanRacCom";
            this.cboCanRacCom.Size = new System.Drawing.Size(210, 24);
            this.cboCanRacCom.TabIndex = 3;

            this.lblXeVao.AutoSize = true;
            this.lblXeVao.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblXeVao.Location = new System.Drawing.Point(15, 108);
            this.lblXeVao.Name = "lblXeVao";
            this.lblXeVao.Text = "Xe vao";

            this.cboXeVao.Location = new System.Drawing.Point(90, 105);
            this.cboXeVao.Name = "cboXeVao";
            this.cboXeVao.Size = new System.Drawing.Size(90, 24);
            this.cboXeVao.TabIndex = 4;

            this.lblHeSoXeVao.AutoSize = true;
            this.lblHeSoXeVao.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeSoXeVao.Location = new System.Drawing.Point(190, 108);
            this.lblHeSoXeVao.Name = "lblHeSoXeVao";
            this.lblHeSoXeVao.Text = "He so";

            this.numHeSoXeVao.Location = new System.Drawing.Point(255, 105);
            this.numHeSoXeVao.Name = "numHeSoXeVao";
            this.numHeSoXeVao.Size = new System.Drawing.Size(65, 22);
            this.numHeSoXeVao.TabIndex = 5;
            this.numHeSoXeVao.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.lblXeRa.AutoSize = true;
            this.lblXeRa.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblXeRa.Location = new System.Drawing.Point(15, 148);
            this.lblXeRa.Name = "lblXeRa";
            this.lblXeRa.Text = "Xe ra";

            this.cboXeRa.Location = new System.Drawing.Point(90, 145);
            this.cboXeRa.Name = "cboXeRa";
            this.cboXeRa.Size = new System.Drawing.Size(90, 24);
            this.cboXeRa.TabIndex = 6;

            this.lblHeSoXeRa.AutoSize = true;
            this.lblHeSoXeRa.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeSoXeRa.Location = new System.Drawing.Point(190, 148);
            this.lblHeSoXeRa.Name = "lblHeSoXeRa";
            this.lblHeSoXeRa.Text = "He so";

            this.numHeSoXeRa.Location = new System.Drawing.Point(255, 145);
            this.numHeSoXeRa.Name = "numHeSoXeRa";
            this.numHeSoXeRa.Size = new System.Drawing.Size(65, 22);
            this.numHeSoXeRa.TabIndex = 7;
            this.numHeSoXeRa.Value = new decimal(new int[] { 1, 0, 0, 0 });

            this.lblBaud.AutoSize = true;
            this.lblBaud.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblBaud.Location = new System.Drawing.Point(15, 188);
            this.lblBaud.Name = "lblBaud";
            this.lblBaud.Text = "Baud rate";

            this.cboBaud.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboBaud.Location = new System.Drawing.Point(110, 185);
            this.cboBaud.Name = "cboBaud";
            this.cboBaud.Size = new System.Drawing.Size(120, 24);
            this.cboBaud.TabIndex = 8;

            // grpTinhTrang
            this.grpTinhTrang.Controls.Add(this.chkCanXeVao);
            this.grpTinhTrang.Controls.Add(this.chkCanXeRa);
            this.grpTinhTrang.Controls.Add(this.chkDoCcs);
            this.grpTinhTrang.Controls.Add(this.chkCanRac);
            this.grpTinhTrang.Location = new System.Drawing.Point(12, 248);
            this.grpTinhTrang.Name = "grpTinhTrang";
            this.grpTinhTrang.Size = new System.Drawing.Size(688, 70);
            this.grpTinhTrang.TabIndex = 2;
            this.grpTinhTrang.TabStop = false;
            this.grpTinhTrang.Text = "Tinh trang cac thiet bi chay tu dong khong cho sua";

            this.chkCanXeVao.AutoSize = true;
            this.chkCanXeVao.Location = new System.Drawing.Point(40, 35);
            this.chkCanXeVao.Name = "chkCanXeVao";
            this.chkCanXeVao.Text = "Can xe vao";

            this.chkCanXeRa.AutoSize = true;
            this.chkCanXeRa.Location = new System.Drawing.Point(180, 35);
            this.chkCanXeRa.Name = "chkCanXeRa";
            this.chkCanXeRa.Text = "Can xe ra";

            this.chkDoCcs.AutoSize = true;
            this.chkDoCcs.Location = new System.Drawing.Point(320, 35);
            this.chkDoCcs.Name = "chkDoCcs";
            this.chkDoCcs.Text = "Do CCS";

            this.chkCanRac.AutoSize = true;
            this.chkCanRac.Location = new System.Drawing.Point(440, 35);
            this.chkCanRac.Name = "chkCanRac";
            this.chkCanRac.Text = "Can rac";

            // grpQuangBao
            this.grpQuangBao.Controls.Add(this.lblCongBaoVe);
            this.grpQuangBao.Controls.Add(this.cboCongBaoVe);
            this.grpQuangBao.Controls.Add(this.lblTramCan);
            this.grpQuangBao.Controls.Add(this.cboTramCan);
            this.grpQuangBao.Controls.Add(this.lblTramCau);
            this.grpQuangBao.Controls.Add(this.cboTramCau);
            this.grpQuangBao.Location = new System.Drawing.Point(12, 324);
            this.grpQuangBao.Name = "grpQuangBao";
            this.grpQuangBao.Size = new System.Drawing.Size(688, 70);
            this.grpQuangBao.TabIndex = 3;
            this.grpQuangBao.TabStop = false;
            this.grpQuangBao.Text = "Cong COM hien thi quang bao";

            this.lblCongBaoVe.AutoSize = true;
            this.lblCongBaoVe.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblCongBaoVe.Location = new System.Drawing.Point(20, 32);
            this.lblCongBaoVe.Name = "lblCongBaoVe";
            this.lblCongBaoVe.Text = "Cong bao ve";

            this.cboCongBaoVe.Location = new System.Drawing.Point(130, 29);
            this.cboCongBaoVe.Name = "cboCongBaoVe";
            this.cboCongBaoVe.Size = new System.Drawing.Size(90, 24);
            this.cboCongBaoVe.TabIndex = 1;

            this.lblTramCan.AutoSize = true;
            this.lblTramCan.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblTramCan.Location = new System.Drawing.Point(250, 32);
            this.lblTramCan.Name = "lblTramCan";
            this.lblTramCan.Text = "Tram can";

            this.cboTramCan.Location = new System.Drawing.Point(345, 29);
            this.cboTramCan.Name = "cboTramCan";
            this.cboTramCan.Size = new System.Drawing.Size(90, 24);
            this.cboTramCan.TabIndex = 2;

            this.lblTramCau.AutoSize = true;
            this.lblTramCau.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblTramCau.Location = new System.Drawing.Point(465, 32);
            this.lblTramCau.Name = "lblTramCau";
            this.lblTramCau.Text = "Tram cau";

            this.cboTramCau.Location = new System.Drawing.Point(560, 29);
            this.cboTramCau.Name = "cboTramCau";
            this.cboTramCau.Size = new System.Drawing.Size(110, 24);
            this.cboTramCau.TabIndex = 3;

            // grpMayQuet
            this.grpMayQuet.Controls.Add(this.lblBaoVeQuet);
            this.grpMayQuet.Controls.Add(this.cboBaoVeQuet);
            this.grpMayQuet.Controls.Add(this.lblTramCanQuet);
            this.grpMayQuet.Controls.Add(this.cboTramCanQuet);
            this.grpMayQuet.Location = new System.Drawing.Point(12, 400);
            this.grpMayQuet.Name = "grpMayQuet";
            this.grpMayQuet.Size = new System.Drawing.Size(688, 70);
            this.grpMayQuet.TabIndex = 4;
            this.grpMayQuet.TabStop = false;
            this.grpMayQuet.Text = "May quet";

            this.lblBaoVeQuet.AutoSize = true;
            this.lblBaoVeQuet.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblBaoVeQuet.Location = new System.Drawing.Point(20, 32);
            this.lblBaoVeQuet.Name = "lblBaoVeQuet";
            this.lblBaoVeQuet.Text = "Bao ve";

            this.cboBaoVeQuet.Location = new System.Drawing.Point(130, 29);
            this.cboBaoVeQuet.Name = "cboBaoVeQuet";
            this.cboBaoVeQuet.Size = new System.Drawing.Size(90, 24);
            this.cboBaoVeQuet.TabIndex = 1;

            this.lblTramCanQuet.AutoSize = true;
            this.lblTramCanQuet.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblTramCanQuet.Location = new System.Drawing.Point(250, 32);
            this.lblTramCanQuet.Name = "lblTramCanQuet";
            this.lblTramCanQuet.Text = "Tram can";

            this.cboTramCanQuet.Location = new System.Drawing.Point(345, 29);
            this.cboTramCanQuet.Name = "cboTramCanQuet";
            this.cboTramCanQuet.Size = new System.Drawing.Size(90, 24);
            this.cboTramCanQuet.TabIndex = 2;

            // lblTrongLuong
            this.lblTrongLuong.AutoSize = false;
            this.lblTrongLuong.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblTrongLuong.BackColor = System.Drawing.Color.Black;
            this.lblTrongLuong.ForeColor = System.Drawing.Color.Lime;
            this.lblTrongLuong.Font = new System.Drawing.Font("Consolas", 26F, System.Drawing.FontStyle.Bold);
            this.lblTrongLuong.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblTrongLuong.Location = new System.Drawing.Point(360, 480);
            this.lblTrongLuong.Name = "lblTrongLuong";
            this.lblTrongLuong.Size = new System.Drawing.Size(340, 52);
            this.lblTrongLuong.Text = "0 kg";

            // lblCongCan
            this.lblCongCan.AutoSize = true;
            this.lblCongCan.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.lblCongCan.ForeColor = System.Drawing.Color.Firebrick;
            this.lblCongCan.Location = new System.Drawing.Point(12, 452);
            this.lblCongCan.Name = "lblCongCan";
            this.lblCongCan.Text = "Cong cau can";

            // cboCongCan
            this.cboCongCan.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboCongCan.Location = new System.Drawing.Point(120, 449);
            this.cboCongCan.Name = "cboCongCan";
            this.cboCongCan.Size = new System.Drawing.Size(110, 24);
            this.cboCongCan.TabIndex = 4;

            // btnTest
            this.btnTest.Location = new System.Drawing.Point(12, 512);
            this.btnTest.Name = "btnTest";
            this.btnTest.Size = new System.Drawing.Size(150, 32);
            this.btnTest.TabIndex = 5;
            this.btnTest.Text = "Ket noi can";
            this.btnTest.UseVisualStyleBackColor = true;
            this.btnTest.Click += new System.EventHandler(this.btnTest_Click);

            // btnDebug
            this.btnDebug.Location = new System.Drawing.Point(240, 449);
            this.btnDebug.Name = "btnDebug";
            this.btnDebug.Size = new System.Drawing.Size(110, 26);
            this.btnDebug.TabIndex = 8;
            this.btnDebug.Text = "Xem du lieu tho";
            this.btnDebug.UseVisualStyleBackColor = true;
            this.btnDebug.Click += new System.EventHandler(this.btnDebug_Click);

            // btnAutoDetect
            this.btnAutoDetect.Location = new System.Drawing.Point(170, 512);
            this.btnAutoDetect.Name = "btnAutoDetect";
            this.btnAutoDetect.Size = new System.Drawing.Size(130, 32);
            this.btnAutoDetect.TabIndex = 9;
            this.btnAutoDetect.Text = "Tu dong do cong";
            this.btnAutoDetect.UseVisualStyleBackColor = true;
            this.btnAutoDetect.Click += new System.EventHandler(this.btnAutoDetect_Click);

            // lblTrangThai
            this.lblTrangThai.AutoSize = true;
            this.lblTrangThai.ForeColor = System.Drawing.Color.DarkBlue;
            this.lblTrangThai.Location = new System.Drawing.Point(12, 552);
            this.lblTrangThai.Name = "lblTrangThai";
            this.lblTrangThai.Text = "Chua ket noi.";

            // btnGhi
            this.btnGhi.Location = new System.Drawing.Point(500, 544);
            this.btnGhi.Name = "btnGhi";
            this.btnGhi.Size = new System.Drawing.Size(90, 30);
            this.btnGhi.TabIndex = 6;
            this.btnGhi.Text = "Ghi";
            this.btnGhi.UseVisualStyleBackColor = true;
            this.btnGhi.Click += new System.EventHandler(this.btnGhi_Click);

            // btnBoQua
            this.btnBoQua.Location = new System.Drawing.Point(600, 544);
            this.btnBoQua.Name = "btnBoQua";
            this.btnBoQua.Size = new System.Drawing.Size(90, 30);
            this.btnBoQua.TabIndex = 7;
            this.btnBoQua.Text = "Bo qua";
            this.btnBoQua.UseVisualStyleBackColor = true;
            this.btnBoQua.Click += new System.EventHandler(this.btnBoQua_Click);

            // FormCauHinh
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(712, 585);
            this.Controls.Add(this.grpChiSo);
            this.Controls.Add(this.grpCongCom);
            this.Controls.Add(this.grpTinhTrang);
            this.Controls.Add(this.grpQuangBao);
            this.Controls.Add(this.grpMayQuet);
            this.Controls.Add(this.lblCongCan);
            this.Controls.Add(this.cboCongCan);
            this.Controls.Add(this.btnDebug);
            this.Controls.Add(this.btnAutoDetect);
            this.Controls.Add(this.lblTrongLuong);
            this.Controls.Add(this.btnTest);
            this.Controls.Add(this.lblTrangThai);
            this.Controls.Add(this.btnGhi);
            this.Controls.Add(this.btnBoQua);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormCauHinh";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Cau hinh ket noi thiet bi can";

            this.grpChiSo.ResumeLayout(false);
            this.grpChiSo.PerformLayout();
            this.grpCongCom.ResumeLayout(false);
            this.grpCongCom.PerformLayout();
            this.grpTinhTrang.ResumeLayout(false);
            this.grpTinhTrang.PerformLayout();
            this.grpQuangBao.ResumeLayout(false);
            this.grpQuangBao.PerformLayout();
            this.grpMayQuet.ResumeLayout(false);
            this.grpMayQuet.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numVaoMin)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numVaoMax)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRaMin)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRaMax)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHeSoXeVao)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHeSoXeRa)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.GroupBox grpChiSo;
        private System.Windows.Forms.Label lblChiSoF;
        private System.Windows.Forms.ComboBox cboChiSoF;
        private System.Windows.Forms.Label lblHeSoK;
        private System.Windows.Forms.ComboBox cboHeSoK;
        private System.Windows.Forms.Label lblVaoMin;
        private System.Windows.Forms.NumericUpDown numVaoMin;
        private System.Windows.Forms.Label lblVaoMax;
        private System.Windows.Forms.NumericUpDown numVaoMax;
        private System.Windows.Forms.Label lblRaMin;
        private System.Windows.Forms.NumericUpDown numRaMin;
        private System.Windows.Forms.Label lblRaMax;
        private System.Windows.Forms.NumericUpDown numRaMax;

        private System.Windows.Forms.GroupBox grpCongCom;
        private System.Windows.Forms.Label lblBx;
        private System.Windows.Forms.ComboBox cboBx;
        private System.Windows.Forms.Label lblPol;
        private System.Windows.Forms.ComboBox cboPol;
        private System.Windows.Forms.Label lblCanRac;
        private System.Windows.Forms.ComboBox cboCanRacCom;
        private System.Windows.Forms.Label lblXeVao;
        private System.Windows.Forms.ComboBox cboXeVao;
        private System.Windows.Forms.Label lblHeSoXeVao;
        private System.Windows.Forms.NumericUpDown numHeSoXeVao;
        private System.Windows.Forms.Label lblXeRa;
        private System.Windows.Forms.ComboBox cboXeRa;
        private System.Windows.Forms.Label lblHeSoXeRa;
        private System.Windows.Forms.NumericUpDown numHeSoXeRa;

        private System.Windows.Forms.GroupBox grpTinhTrang;
        private System.Windows.Forms.CheckBox chkCanXeVao;
        private System.Windows.Forms.CheckBox chkCanXeRa;
        private System.Windows.Forms.CheckBox chkDoCcs;
        private System.Windows.Forms.CheckBox chkCanRac;

        private System.Windows.Forms.GroupBox grpQuangBao;
        private System.Windows.Forms.Label lblCongBaoVe;
        private System.Windows.Forms.ComboBox cboCongBaoVe;
        private System.Windows.Forms.Label lblTramCan;
        private System.Windows.Forms.ComboBox cboTramCan;
        private System.Windows.Forms.Label lblTramCau;
        private System.Windows.Forms.ComboBox cboTramCau;

        private System.Windows.Forms.GroupBox grpMayQuet;
        private System.Windows.Forms.Label lblBaoVeQuet;
        private System.Windows.Forms.ComboBox cboBaoVeQuet;
        private System.Windows.Forms.Label lblTramCanQuet;
        private System.Windows.Forms.ComboBox cboTramCanQuet;

        private System.Windows.Forms.Label lblBaud;
        private System.Windows.Forms.ComboBox cboBaud;
        private System.Windows.Forms.Label lblCongCan;
        private System.Windows.Forms.ComboBox cboCongCan;
        private System.Windows.Forms.Button btnDebug;
        private System.Windows.Forms.Button btnAutoDetect;
        private System.Windows.Forms.Label lblTrongLuong;

        private System.Windows.Forms.Button btnGhi;
        private System.Windows.Forms.Button btnBoQua;
        private System.Windows.Forms.Button btnTest;
        private System.Windows.Forms.Label lblTrangThai;
    }
}
