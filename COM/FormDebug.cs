using System;
using System.Text;
using System.Windows.Forms;

namespace CanDienTu
{
    /// <summary>
    /// Cua so hien thi du lieu tho nhan tu cong COM (text + hex)
    /// de chan doan vi sao phan mem khong len so.
    /// </summary>
    public class FormDebug : Form
    {
        private readonly TextBox _txt;
        private readonly Label _lblInfo;
        private long _totalBytes;

        public FormDebug()
        {
            Text = "Du lieu tho tu cong COM (debug)";
            Width = 640;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;

            _lblInfo = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "Dang cho du lieu... (neu trong = cong khong nhan byte)",
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            _txt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                WordWrap = false,
                Font = new System.Drawing.Font("Consolas", 10F)
            };

            var btnClear = new Button { Text = "Xoa", Dock = DockStyle.Bottom, Height = 30 };
            btnClear.Click += (s, e) => { _txt.Clear(); _totalBytes = 0; UpdateInfo(); };

            Controls.Add(_txt);
            Controls.Add(btnClear);
            Controls.Add(_lblInfo);
        }

        /// <summary>Hien thi mot goi byte nhan duoc.</summary>
        public void AppendBytes(byte[] bytes)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<byte[]>(AppendBytes), new object[] { bytes });
                return;
            }

            _totalBytes += bytes.Length;

            var hex = new StringBuilder();
            var txt = new StringBuilder();
            foreach (byte b in bytes)
            {
                hex.Append(b.ToString("X2")).Append(' ');
                txt.Append(b >= 32 && b < 127 ? (char)b : '.');
            }

            _txt.AppendText("HEX : " + hex.ToString().TrimEnd() + "\r\n");
            _txt.AppendText("TEXT: " + txt + "\r\n");
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            _lblInfo.Text = "Tong byte nhan: " + _totalBytes +
                "  (neu = 0 nghia la cong khong nhan du lieu: sai cong/baud/day)";
        }
    }
}
