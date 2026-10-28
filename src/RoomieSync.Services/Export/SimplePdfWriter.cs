using System.Globalization;
using System.Text;

namespace RoomieSync.Services.Export;

internal sealed class SimplePdfWriter
{
    private const float PageWidth = 595, PageHeight = 842, Margin = 50;
    private readonly List<StringBuilder> _pages = new();
    private StringBuilder _page = null!;
    private float _y;

    private static readonly Dictionary<char, byte> CyrillicToByte = BuildCyrillicMap();

    private const string CyrillicEncodingObj = """
        << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [
        165 /afii10050
        170 /afii10053
        175 /afii10056
        178 /afii10055
        179 /afii10103
        180 /afii10098
        184 /afii10071
        185 /afii61352
        186 /afii10101
        191 /afii10104
        192 /afii10017 /afii10018 /afii10019 /afii10020 /afii10021 /afii10022 /afii10024 /afii10025
        /afii10026 /afii10027 /afii10028 /afii10029 /afii10030 /afii10031 /afii10032 /afii10033
        /afii10034 /afii10035 /afii10036 /afii10037 /afii10038 /afii10039 /afii10040 /afii10041
        /afii10042 /afii10043 /afii10044 /afii10045 /afii10046 /afii10047 /afii10048 /afii10049
        /afii10065 /afii10066 /afii10067 /afii10068 /afii10069 /afii10070 /afii10072 /afii10073
        /afii10074 /afii10075 /afii10076 /afii10077 /afii10078 /afii10079 /afii10080 /afii10081
        /afii10082 /afii10083 /afii10084 /afii10085 /afii10086 /afii10087 /afii10088 /afii10089
        /afii10090 /afii10091 /afii10092 /afii10093 /afii10094 /afii10095 /afii10096 /afii10097
        ] >>
        """;

    public SimplePdfWriter() => NewPage();

    private void NewPage()
    {
        _page = new StringBuilder();
        _pages.Add(_page);
        _y = PageHeight - Margin;
    }

    private void Ensure(float height)
    {
        if (_y - height < Margin) NewPage();
    }

    private void Write(float x, float size, bool bold, string text, string color = "0.12 0.13 0.21")
    {
        _page.Append(CultureInfo.InvariantCulture, $"BT {color} rg /{(bold ? "F2" : "F1")} {size} Tf {Margin + x:0.##} {_y:0.##} Td ({Escape(text)}) Tj ET\n");
    }

    public void Heading(string text)
    {
        Ensure(30);
        Write(0, 18, true, text, "0.94 0.48 0.25");
        _y -= 26;
    }

    public void Text(string text)
    {
        Ensure(16);
        Write(0, 10, false, text, "0.45 0.47 0.55");
        _y -= 16;
    }

    public void Space() => _y -= 10;

    public void Row(float[] columns, bool header, params string[] cells)
    {
        Ensure(18);
        for (var i = 0; i < cells.Length && i < columns.Length; i++)
            Write(columns[i], header ? 9 : 10, header, cells[i], header ? "0.45 0.47 0.55" : "0.12 0.13 0.21");
        _y -= 6;
        _page.Append(CultureInfo.InvariantCulture, $"0.92 0.9 0.88 RG 0.5 w {Margin} {_y:0.##} m {PageWidth - Margin} {_y:0.##} l S\n");
        _y -= 12;
    }

    public byte[] Build()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "",
            "<< /Type /Font /Subtype /TrueType /BaseFont /Arial /Encoding 5 0 R >>",
            "<< /Type /Font /Subtype /TrueType /BaseFont /Arial,Bold /Encoding 5 0 R >>",
            CyrillicEncodingObj
        };
        var kids = new List<int>();
        foreach (var p in _pages)
        {
            var content = p.ToString();
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
            var contentId = objects.Count;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>");
            kids.Add(objects.Count);
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>";

        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        W("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string Escape(string s)
    {
        var normalized = s
            .Replace("₴", "UAH ")
            .Replace("€", "EUR ")
            .Replace("£", "GBP ")
            .Replace("zł", "PLN ")
            .Replace("—", "-")
            .Replace("–", "-")
            .Replace("·", "-")
            .Replace("…", "...");

        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (ch is '(' or ')' or '\\')
            {
                sb.Append('\\').Append(ch);
            }
            else if (ch <= 126)
            {
                sb.Append(ch);
            }
            else if (CyrillicToByte.TryGetValue(ch, out var b))
            {
                sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            }
            else if (ch <= 255)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static Dictionary<char, byte> BuildCyrillicMap()
    {
        return new Dictionary<char, byte>
        {
            ['А'] = 192,
            ['Б'] = 193,
            ['В'] = 194,
            ['Г'] = 195,
            ['Ґ'] = 165,
            ['Д'] = 196,
            ['Е'] = 197,
            ['Є'] = 170,
            ['Ж'] = 198,
            ['З'] = 199,
            ['И'] = 200,
            ['І'] = 178,
            ['Ї'] = 175,
            ['Й'] = 201,
            ['К'] = 202,
            ['Л'] = 203,
            ['М'] = 204,
            ['Н'] = 205,
            ['О'] = 206,
            ['П'] = 207,
            ['Р'] = 208,
            ['С'] = 209,
            ['Т'] = 210,
            ['У'] = 211,
            ['Ф'] = 212,
            ['Х'] = 213,
            ['Ц'] = 214,
            ['Ч'] = 215,
            ['Ш'] = 216,
            ['Щ'] = 217,
            ['Ь'] = 220,
            ['Ю'] = 222,
            ['Я'] = 223,

            ['а'] = 224,
            ['б'] = 225,
            ['в'] = 226,
            ['г'] = 227,
            ['ґ'] = 180,
            ['д'] = 228,
            ['е'] = 229,
            ['є'] = 186,
            ['ж'] = 230,
            ['з'] = 231,
            ['и'] = 232,
            ['і'] = 179,
            ['ї'] = 191,
            ['й'] = 233,
            ['к'] = 234,
            ['л'] = 235,
            ['м'] = 236,
            ['н'] = 237,
            ['о'] = 238,
            ['п'] = 239,
            ['р'] = 240,
            ['с'] = 241,
            ['т'] = 242,
            ['у'] = 243,
            ['ф'] = 244,
            ['х'] = 245,
            ['ц'] = 246,
            ['ч'] = 247,
            ['ш'] = 248,
            ['щ'] = 249,
            ['ь'] = 252,
            ['ю'] = 254,
            ['я'] = 255,

            ['№'] = 185
        };
    }
}