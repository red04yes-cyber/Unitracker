using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public static class StudyProductivityExportService
{
    private static readonly string[] Headers = ["CourseId", "Title", "StartsAt", "DurationMinutes", "Attended", "Notes"];

    public static byte[] ExportAttendanceXlsx(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var rows = new List<string[]> { Headers };
        rows.AddRange(data.Productivity.Attendance.OrderBy(x => x.StartsAt).Select(x => new[]
        {
            x.CourseId, x.Title, x.StartsAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            x.DurationMinutes.ToString(CultureInfo.InvariantCulture), x.Attended ? "Sì" : "No", x.Notes ?? string.Empty
        }));

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";

        var sheetRows = new XElement(main + "sheetData");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new XElement(main + "row", new XAttribute("r", rowIndex + 1));
            for (var colIndex = 0; colIndex < rows[rowIndex].Length; colIndex++)
            {
                var cellRef = $"{ColumnName(colIndex + 1)}{rowIndex + 1}";
                row.Add(new XElement(main + "c", new XAttribute("r", cellRef), new XAttribute("t", "inlineStr"),
                    new XElement(main + "is", new XElement(main + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), rows[rowIndex][colIndex]))));
            }
            sheetRows.Add(row);
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddXml(archive, "[Content_Types].xml", new XDocument(
                new XElement(content + "Types",
                    new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(content + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
            AddXml(archive, "_rels/.rels", new XDocument(new XElement(packageRel + "Relationships",
                new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")))));
            AddXml(archive, "xl/workbook.xml", new XDocument(new XElement(main + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", rel),
                new XElement(main + "sheets", new XElement(main + "sheet",
                    new XAttribute("name", "Presenze"), new XAttribute("sheetId", 1), new XAttribute(rel + "id", "rId1"))))));
            AddXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(packageRel + "Relationships",
                new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")))));
            AddXml(archive, "xl/worksheets/sheet1.xml", new XDocument(new XElement(main + "worksheet", sheetRows)));
        }
        return output.ToArray();
    }

    public static byte[] ExportAttendancePdf(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var lines = new List<string>
        {
            "REGISTRO PRESENZE - UNITRACKER",
            $"Esportato il {DateTime.Now:dd/MM/yyyy HH:mm}",
            "Corso | Lezione | Data e ora | Minuti | Presente | Note",
            new string('-', 90)
        };
        lines.AddRange(data.Productivity.Attendance.OrderBy(x => x.StartsAt).Select(x =>
            $"{CourseName(data, x.CourseId)} | {x.Title} | {x.StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} | {x.DurationMinutes} | {(x.Attended ? "Si" : "No")} | {x.Notes}"));
        if (lines.Count == 4) lines.Add("Nessuna presenza registrata.");

        var wrapped = lines.SelectMany(x => Wrap(Transliterate(x), 105)).ToList();
        var pages = wrapped.Chunk(48).ToArray();
        var objects = new List<byte[]>();
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageIds = Enumerable.Range(0, pages.Length).Select(i => 4 + i * 2).ToArray();
        objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Length} >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var content = new StringBuilder("BT /F1 9 Tf 42 790 Td 13 TL\n");
            foreach (var line in pages[pageIndex])
                content.Append('(').Append(EscapePdf(line)).Append(") Tj T*\n");
            content.Append("ET");
            var contentBytes = Encoding.ASCII.GetBytes(content.ToString());
            var pageId = pageIds[pageIndex];
            var contentId = pageId + 1;
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>"));
            var streamHeader = Encoding.ASCII.GetBytes($"<< /Length {contentBytes.Length} >>\nstream\n");
            var streamFooter = Encoding.ASCII.GetBytes("\nendstream");
            objects.Add(streamHeader.Concat(contentBytes).Concat(streamFooter).ToArray());
        }

        using var pdf = new MemoryStream();
        WriteAscii(pdf, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            WriteAscii(pdf, $"{i + 1} 0 obj\n");
            pdf.Write(objects[i]);
            WriteAscii(pdf, "\nendobj\n");
        }
        var xrefOffset = pdf.Position;
        WriteAscii(pdf, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(pdf, $"{offset:0000000000} 00000 n \n");
        WriteAscii(pdf, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return pdf.ToArray();
    }

    private static string CourseName(StudyData data, string courseId) =>
        data.Courses.FirstOrDefault(x => x.Id == courseId)?.Name ?? courseId;

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer);
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        for (var start = 0; start < value.Length; start += width)
            yield return value.Substring(start, Math.Min(width, value.Length - start));
    }

    private static string Transliterate(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var ascii = new string(normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return new string(ascii.Select(ch => ch <= 127 ? ch : '?').ToArray());
    }

    private static string EscapePdf(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static void WriteAscii(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));
}
