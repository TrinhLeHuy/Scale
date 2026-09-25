namespace TTC.Weighing.Scenario;

/// <summary>Ghi tiến trình kịch bản ra console và đếm số bước không đạt mong đợi.</summary>
public sealed class ScenarioLog
{
    private int _step;

    public int Failures { get; private set; }

    public void Step(string title)
    {
        _step++;
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"BƯỚC {_step:D2}  {title}");
        Console.ResetColor();
        Console.WriteLine(new string('-', 78));
    }

    public void Info(string text) => Console.WriteLine($"   {text}");

    public void Detail(string text) => Console.WriteLine($"      {text}");

    /// <summary>Khẳng định một điều kiện nghiệp vụ phải đúng, ghi rõ căn cứ trong specs.</summary>
    public void Expect(bool condition, string description)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   ĐẠT   {description}");
        }
        else
        {
            Failures++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   SAI   {description}");
        }

        Console.ResetColor();
    }

    public void Section(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"=== {title} ===");
        Console.ResetColor();
    }

    public void Summary()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));

        if (Failures == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"KỊCH BẢN ĐẠT — {_step} bước, không có khẳng định nào sai.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"KỊCH BẢN CÓ VẤN ĐỀ — {Failures} khẳng định sai trên {_step} bước.");
        }

        Console.ResetColor();
        Console.WriteLine(new string('=', 78));
    }
}
