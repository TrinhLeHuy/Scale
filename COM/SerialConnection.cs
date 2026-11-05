using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace CanDienTu
{
    /// <summary>
    /// Du lieu trong luong doc duoc tu dau can.
    /// </summary>
    public class WeightEventArgs : EventArgs
    {
        public double Weight { get; private set; }
        public string Raw { get; private set; }

        public WeightEventArgs(double weight, string raw)
        {
            Weight = weight;
            Raw = raw;
        }
    }

    /// <summary>
    /// Quan ly ket noi va doc du lieu can qua cong COM (RS-232).
    /// Ho tro cac giao thuc text pho bien (Toledo, Keli, A12...) bang cach
    /// bat so trong khung du lieu gui ve.
    /// </summary>
    public class SerialConnection : IDisposable
    {
        private SerialPort _port;
        private readonly StringBuilder _buffer = new StringBuilder();

        // Khung dau can dang: dau [+/-] theo sau la cac chu so, vi du "+0000140".
        // Uu tien bat cum co dau; neu khong co thi bat so bat ky.
        private static readonly Regex SignedWeightRegex =
            new Regex(@"[-+]\s*\d{1,8}(?:\.\d+)?", RegexOptions.Compiled);
        private static readonly Regex WeightRegex =
            new Regex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);

        /// <summary>Phat sinh khi doc duoc mot gia tri trong luong moi.</summary>
        public event EventHandler<WeightEventArgs> WeightReceived;

        /// <summary>Phat sinh khi co du lieu tho (ca khung chua tach).</summary>
        public event EventHandler<string> RawReceived;

        /// <summary>Phat sinh voi byte tho nhan duoc (de debug/hex).</summary>
        public event EventHandler<byte[]> RawBytesReceived;

        /// <summary>Phat sinh khi co loi ket noi.</summary>
        public event EventHandler<Exception> ConnectionError;

        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        public string PortName
        {
            get { return _port != null ? _port.PortName : null; }
        }

        /// <summary>
        /// Lay danh sach cong COM dang co tren may.
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Tu dong do tim cong dang nhan du lieu can.
        /// Quet cac cong COM, mo thu voi baud cho truoc, cong nao nhan
        /// duoc byte thi tra ve ten cong do. Tra ve null neu khong co.
        /// </summary>
        public static string DoTimCong(int baudRate, out string chiTiet)
        {
            var sb = new StringBuilder();
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
            string found = null;

            foreach (string p in ports)
            {
                var sp = new SerialPort(p, baudRate, Parity.None, 8, StopBits.One);
                sp.ReadTimeout = 500;
                sp.DtrEnable = true;
                sp.RtsEnable = true;
                try
                {
                    sp.Open();
                    System.Threading.Thread.Sleep(1200);
                    int n = sp.BytesToRead;
                    sb.AppendLine(p + ": " + n + " byte");
                    if (n > 0 && found == null)
                        found = p;
                }
                catch (Exception ex)
                {
                    sb.AppendLine(p + ": loi (" + ex.Message + ")");
                }
                finally
                {
                    try { if (sp.IsOpen) sp.Close(); } catch { }
                    sp.Dispose();
                }
            }

            chiTiet = sb.ToString();
            return found;
        }

        /// <summary>
        /// Mo ket noi toi dau can.
        /// </summary>
        public void Open(string portName, int baudRate, int dataBits,
                         Parity parity, StopBits stopBits)
        {
            if (string.IsNullOrEmpty(portName))
                throw new ArgumentException("Chua chon cong COM.", "portName");

            Close();

            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
            _port.ReadTimeout = 1000;
            _port.WriteTimeout = 1000;
            _port.Handshake = Handshake.None;
            _port.DtrEnable = true;
            _port.RtsEnable = true;
            _port.DataReceived += PortOnDataReceived;
            _port.ErrorReceived += PortOnErrorReceived;

            _port.Open();
        }

        public void Close()
        {
            if (_port != null)
            {
                try
                {
                    _port.DataReceived -= PortOnDataReceived;
                    _port.ErrorReceived -= PortOnErrorReceived;
                    if (_port.IsOpen)
                        _port.Close();
                }
                catch
                {
                    // bo qua loi khi dong cong
                }
                finally
                {
                    _port.Dispose();
                    _port = null;
                }
            }
            _buffer.Length = 0;
        }

        /// <summary>
        /// Gui lenh toi dau can (vi du lenh yeu cau can gui so lieu).
        /// </summary>
        public void Send(string command)
        {
            if (IsOpen)
                _port.Write(command);
        }

        private void PortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int n = _port.BytesToRead;
                if (n <= 0)
                    return;

                byte[] bytes = new byte[n];
                int read = _port.Read(bytes, 0, n);
                if (read <= 0)
                    return;
                if (read != n)
                    Array.Resize(ref bytes, read);

                var rawb = RawBytesReceived;
                if (rawb != null)
                    rawb(this, bytes);

                // Mot so dau can set bit thu 8 (parity bit) tren ky tu ASCII,
                // vi du '1' (0x31) bi gui thanh 0xB1. Mask 0x7F de khoi phuc.
                byte[] masked = new byte[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                    masked[i] = (byte)(bytes[i] & 0x7F);

                string chunk = Encoding.ASCII.GetString(masked);

                var raw = RawReceived;
                if (raw != null)
                    raw(this, chunk);

                _buffer.Append(chunk);

                // Cac dau can thuong ket thuc khung bang CR, LF hoac ETX.
                string data = _buffer.ToString();
                int idx;
                while ((idx = IndexOfTerminator(data)) >= 0)
                {
                    string frame = data.Substring(0, idx);
                    data = data.Substring(idx + 1);
                    ParseFrame(frame);
                }
                _buffer.Length = 0;
                _buffer.Append(data);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        private static int IndexOfTerminator(string data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];
                if (c == '\r' || c == '\n' || c == '\x03')
                    return i;
            }
            return -1;
        }

        private void ParseFrame(string frame)
        {
            if (string.IsNullOrEmpty(frame))
                return;

            // Chi giu ky tu ASCII in duoc (loai byte nhieu >127 o dau khung).
            var sb = new StringBuilder(frame.Length);
            foreach (char c in frame)
            {
                if (c == '+' || c == '-' || c == '.' ||
                    (c >= '0' && c <= '9'))
                    sb.Append(c);
            }
            string clean = sb.ToString();
            if (clean.Length == 0)
                return;

            // Uu tien cum co dau (+/-), vi dau can gui dang "+0000140".
            Match m = SignedWeightRegex.Match(clean);
            if (!m.Success)
                m = WeightRegex.Match(clean);
            if (!m.Success)
                return;

            string num = m.Value.Replace(" ", "");
            double value;
            if (double.TryParse(num, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out value))
            {
                var handler = WeightReceived;
                if (handler != null)
                    handler(this, new WeightEventArgs(value, frame));
            }
        }

        private void PortOnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            RaiseError(new Exception("Loi cong COM: " + e.EventType));
        }

        private void RaiseError(Exception ex)
        {
            var handler = ConnectionError;
            if (handler != null)
                handler(this, ex);
        }

        public void Dispose()
        {
            Close();
        }
    }
}
