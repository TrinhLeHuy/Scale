namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Ô số có thể khoá chống sửa tay nhưng vẫn đọc rõ (không xám như Enabled = false).
/// Khi <see cref="Locked"/>: không gõ được, mũi tên / con lăn chuột / nút tăng giảm không đổi giá trị.
/// Code vẫn gán <see cref="NumericUpDown.Value"/> được (số đọc từ máy đo).
/// </summary>
public sealed class LockableNumericUpDown : NumericUpDown
{
    private bool _locked;

    public bool Locked
    {
        get => _locked;
        set
        {
            if (_locked == value) return;
            _locked = value;
            ReadOnly = value;
            InterceptArrowKeys = !value;
            LockedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? LockedChanged;

    public override void UpButton()
    {
        if (!_locked) base.UpButton();
    }

    public override void DownButton()
    {
        if (!_locked) base.DownButton();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_locked)
        {
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            return;
        }

        base.OnMouseWheel(e);
    }
}
