using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// ---------------------------
// הגדרה של הסיומות
// ---------------------------
var codeExtensions = new[] { ".cs", ".js", ".html", ".css", ".ts", ".java", ".cpp" };

// ---------------------------
// פונקציה למציאת קבצים
// ---------------------------
IEnumerable<string> GetCodeFiles(string rootPath, string[] languages)
{
    var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !f.Contains(Path.DirectorySeparatorChar + "debug" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(f => codeExtensions.Contains(Path.GetExtension(f).ToLower()));

    if (languages.Length == 1 && languages[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        return files;

    return files.Where(f =>
        languages.Any(lang => Path.GetExtension(f).Trim('.').Equals(lang, StringComparison.OrdinalIgnoreCase)));
}

// ---------------------------
// פונקציה להסרת שורות ריקות
// ---------------------------
IEnumerable<string> RemoveEmptyLines(IEnumerable<string> lines)
{
    return lines.Where(line => !string.IsNullOrWhiteSpace(line));
}

// ---------------------------
// הגדרת אופציות ופקודת bundle
// ---------------------------
var languageOption = new Option<string[]>(
    aliases: new[] { "--language", "-l" },
    description: "רשימת שפות תכנות. כתבו 'all' לכל הקבצים")
{
    IsRequired = true,
    Arity = ArgumentArity.OneOrMore
};

var outputOption = new Option<string>(new[] { "--output", "-o" }, "שם קובץ ה-bundle או נתיב מלא");
var noteOption = new Option<bool>(new[] { "--note", "-n" }, "להוסיף הערת מקור קבצים בקובץ ה-bundle");
var sortOption = new Option<string>(new[] { "--sort", "-s" }, getDefaultValue: () => "name", "סדר מיון: name או type");
var removeEmptyLinesOption = new Option<bool>(new[] { "--remove-empty-lines", "-r" }, "האם למחוק שורות ריקות");
var authorOption = new Option<string>(new[] { "--author", "-a" }, "שם היוצר");

var bundleCommand = new Command("bundle", "ממזג קבצי קוד לקובץ אחד");
bundleCommand.AddOption(languageOption);
bundleCommand.AddOption(outputOption);
bundleCommand.AddOption(noteOption);
bundleCommand.AddOption(sortOption);
bundleCommand.AddOption(removeEmptyLinesOption);
bundleCommand.AddOption(authorOption);

// ---------------------------
// Handler של הפקודה bundle
// ---------------------------
bundleCommand.SetHandler(
    async (string[] language, string output, bool note, string sort, bool removeEmptyLines, string author) =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(output))
                output = "bundle_output.txt";

            var files = GetCodeFiles(Directory.GetCurrentDirectory(), language);

            if (!files.Any())
            {
                Console.WriteLine("⚠️ לא נמצאו קבצים תואמים.");
                return;
            }

            files = sort.Equals("type", StringComparison.OrdinalIgnoreCase)
                ? files.OrderBy(f => Path.GetExtension(f))
                : files.OrderBy(f => Path.GetFileName(f));

            using var writer = new StreamWriter(output, false);

            if (!string.IsNullOrWhiteSpace(author))
                await writer.WriteLineAsync($"// Author: {author}");

            await writer.WriteLineAsync($"// Created on: {DateTime.Now}");
            await writer.WriteLineAsync("// -----------------------------");

            foreach (var file in files)
            {
                if (note)
                    await writer.WriteLineAsync($"// Source: {Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}");

                var lines = File.ReadAllLines(file);

                if (removeEmptyLines)
                    lines = RemoveEmptyLines(lines).ToArray();

                foreach (var line in lines)
                    await writer.WriteLineAsync(line);

                await writer.WriteLineAsync("\n// ===== End of File =====\n");
            }

            Console.WriteLine($"✅ נוצר קובץ Bundle בהצלחה: {Path.GetFullPath(output)}");
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("❌ אין הרשאה לכתוב במיקום שנבחר.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ שגיאה: {ex.Message}");
        }
    },
    languageOption, outputOption, noteOption, sortOption, removeEmptyLinesOption, authorOption
);

// ---------------------------
// פקודת create-rsp
// ---------------------------
var createRspCommand = new Command("create-rsp", "יוצר response file עבור הפקודה bundle");

createRspCommand.SetHandler(() =>
{
    Console.WriteLine("יצירת קובץ Response עבור הפקודה bundle");

    Console.Write("Enter languages (comma-separated, or 'all'): ");
    var languageInput = Console.ReadLine()?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.Trim()).ToArray() ?? new string[] { "all" };

    Console.Write("Enter output file name (with or without path): ");
    var output = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(output))
        output = "bundle_output.txt";

    Console.Write("Include note? (true/false): ");
    var noteInput = Console.ReadLine()?.Trim().ToLower() == "true";

    Console.Write("Sort (name/type) [default: name]: ");
    var sort = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(sort))
        sort = "name";

    Console.Write("Remove empty lines? (true/false): ");
    var removeEmptyLines = Console.ReadLine()?.Trim().ToLower() == "true";

    Console.Write("Author name (optional): ");
    var author = Console.ReadLine();

    // בניית הפקודה המלאה
    var cmdParts = new List<string> { "bundle" };
    cmdParts.Add("-l " + string.Join(" ", languageInput));
    cmdParts.Add("-o " + output);

    if (noteInput)
        cmdParts.Add("-n");

    cmdParts.Add("-s " + sort);

    if (removeEmptyLines)
        cmdParts.Add("-r");

    if (!string.IsNullOrWhiteSpace(author))
        cmdParts.Add("-a " + author);

    var finalCommand = string.Join(" ", cmdParts);

    // יצירת קובץ ה-rsp
    var rspFileName = "bundle.rsp";
    File.WriteAllText(rspFileName, finalCommand);

    Console.WriteLine($"✅ קובץ response נוצר בהצלחה: {rspFileName}");
});

// ---------------------------
// Root והרצה
// ---------------------------
var rootCommand = new RootCommand("אפליקציה למיזוג קבצי קוד");
rootCommand.AddCommand(bundleCommand);
rootCommand.AddCommand(createRspCommand);

await rootCommand.InvokeAsync(args);
