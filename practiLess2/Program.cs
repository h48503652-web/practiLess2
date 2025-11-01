

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// ===============================
// HtmlElement Class
// ===============================
public class HtmlElement
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
    public List<string> Classes { get; set; } = new();
    public string InnerHtml { get; set; } = string.Empty;
    public HtmlElement? Parent { get; set; }
    public List<HtmlElement> Children { get; set; } = new();

    public override string ToString() =>
        $"<{Name}{(Attributes.Count > 0 ? " " + string.Join(" ", Attributes.Select(a => $"{a.Key}='{a.Value}'")) : string.Empty)}>";

    public IEnumerable<HtmlElement> Descendants()
    {
        Queue<HtmlElement> queue = new();
        queue.Enqueue(this);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var c in cur.Children)
            {
                yield return c;
                queue.Enqueue(c);
            }
        }
    }

    public IEnumerable<HtmlElement> Ancestors()
    {
        HtmlElement? current = this.Parent;
        while (current != null)
        {
            yield return current;
            current = current.Parent;
        }
    }
}

// ===============================
// HtmlHelper Singleton
// ===============================
public sealed class HtmlHelper
{
    private static readonly Lazy<HtmlHelper> instance = new(() => new HtmlHelper());
    public static HtmlHelper Instance => instance.Value;

    public HashSet<string> Tags { get; private set; } = new();
    public HashSet<string> VoidTags { get; private set; } = new();

    private HtmlHelper()
    {
        try
        {
            string basePath = AppContext.BaseDirectory;
            string tagsPath = Path.Combine(basePath, "html_tags.json");
            string voidTagsPath = Path.Combine(basePath, "html_void_tags.json");

            if (File.Exists(tagsPath))
                Tags = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(tagsPath)) ?? new();

            if (File.Exists(voidTagsPath))
                VoidTags = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(voidTagsPath)) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading HTML tag data: {ex.Message}");
        }
    }
}

// ===============================
// HtmlSerializer Class
// ===============================
public class HtmlSerializer
{
    private static int idCounter = 1;

    public async Task<HtmlElement?> LoadAsync(string url)
    {
        using HttpClient client = new();
        string html = await client.GetStringAsync(url);
        return ParseHtml(html);
    }

    public HtmlElement? ParseHtml(string html)
    {
        html = Regex.Replace(html, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        Stack<HtmlElement> stack = new();
        HtmlElement? root = null;

        var tagRegex = new Regex(@"<(/)?([a-zA-Z0-9]+)([^>]*)/?>");
        int lastIndex = 0;

        foreach (Match match in tagRegex.Matches(html))
        {
            string beforeText = html[lastIndex..match.Index].Trim();
            if (stack.Count > 0 && !string.IsNullOrEmpty(beforeText))
                stack.Peek().InnerHtml += (stack.Peek().InnerHtml.Length > 0 ? " " : "") + beforeText;

            lastIndex = match.Index + match.Length;

            bool closing = match.Groups[1].Value == "/";
            string tagName = match.Groups[2].Value.ToLower();
            string attrText = match.Groups[3].Value;

            bool selfClosing = attrText.EndsWith("/") || HtmlHelper.Instance.VoidTags.Contains(tagName);

            if (closing)
            {
                if (stack.Count > 0)
                    stack.Pop();
            }
            else
            {
                HtmlElement el = new()
                {
                    Id = idCounter++,
                    Name = tagName,
                    Attributes = ParseAttributes(attrText),
                    Classes = ParseClasses(attrText)
                };

                if (stack.Count == 0)
                    root = el;
                else
                {
                    el.Parent = stack.Peek();
                    stack.Peek().Children.Add(el);
                }

                if (!selfClosing)
                    stack.Push(el);
            }
        }

        return root;
    }

    private Dictionary<string, string> ParseAttributes(string text)
    {
        var attrs = new Dictionary<string, string>();
        var attrRegex = new Regex(@"(\w+)(=""([^""]*)"")?");
        foreach (Match m in attrRegex.Matches(text))
        {
            string key = m.Groups[1].Value;
            string val = m.Groups[3].Success ? m.Groups[3].Value : string.Empty;
            attrs[key] = val;
        }
        return attrs;
    }

    private List<string> ParseClasses(string text)
    {
        var classRegex = new Regex(@"class=""([^""]+)""");
        var match = classRegex.Match(text);
        if (!match.Success) return new();
        return match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}

// ===============================
// Selector Class
// ===============================
public class Selector
{
    public string? TagName { get; set; }
    public string? Id { get; set; }
    public List<string> Classes { get; set; } = new();
    public Selector? Parent { get; set; }
    public Selector? Child { get; set; }

    public static Selector ParseFull(string selectorString)
    {
        string[] parts = selectorString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Selector? root = null;
        Selector? current = null;

        foreach (var part in parts)
        {
            var sel = ParseSingle(part);

            if (root == null)
            {
                root = sel;
                current = sel;
            }
            else
            {
                current!.Child = sel;
                sel.Parent = current;
                current = sel;
            }
        }

        return root!;
    }

    private static Selector ParseSingle(string part)
    {
        var sel = new Selector();
        var match = Regex.Match(part,
            @"^(?<tag>[a-zA-Z0-9]*)?(#(?<id>[\w-]+))?(\.(?<classes>[\w-.]+))?");

        if (match.Success)
        {
            if (!string.IsNullOrEmpty(match.Groups["tag"].Value))
                sel.TagName = match.Groups["tag"].Value;
            if (!string.IsNullOrEmpty(match.Groups["id"].Value))
                sel.Id = match.Groups["id"].Value;
            if (match.Groups["classes"].Success)
                sel.Classes = match.Groups["classes"].Value.Split('.').ToList();
        }
        return sel;
    }

    public bool Matches(HtmlElement el)
    {
        if (TagName != null && el.Name != TagName)
            return false;
        if (Id != null)
        {
            if (!el.Attributes.ContainsKey("id") || el.Attributes["id"] != Id)
                return false;
        }
        if (Classes.Count > 0)
        {
            if (!Classes.All(c => el.Classes.Contains(c)))
                return false;
        }
        return true;
    }
}

// ===============================
// HtmlQueryService Class
// ===============================
public class HtmlQueryService
{
    public List<HtmlElement> Query(HtmlElement root, string selectorString)
    {
        Selector selectorTree = Selector.ParseFull(selectorString);
        HashSet<HtmlElement> results = new();
        Search(root, selectorTree, results);
        return results.ToList();
    }

    private void Search(HtmlElement node, Selector selector, HashSet<HtmlElement> results)
    {
        // אם node תואם לסלקטור הנוכחי
        if (selector.Matches(node))
        {
            if (selector.Child == null)
            {
                results.Add(node);
            }
            else
            {
                foreach (var child in node.Descendants()) // בדיקה בעומק לכל הצאצאים
                    Search(child, selector.Child, results);
                return;
            }
        }

        foreach (var child in node.Children)
            Search(child, selector, results);
    }
}

// ===============================
// Program Demo
// ===============================
public class Program
{
    public static async Task Main()
    {
        HtmlSerializer serializer = new();

        string url = "https://learn.malkabruk.co.il/practicode";
        string html = await LoadHtmlFromUrl(url);

        var root = serializer.ParseHtml(html);
        if (root == null)
        {
            Console.WriteLine("ERROR: root is null – parsing failed.");
            return;
        }

        Console.WriteLine($"Root Tag: {root.Name}");
        Console.WriteLine($"Root Children Count: {root.Children.Count}");
        Console.WriteLine("-------------------------------------------");

        var selectorTest = Selector.ParseFull("div#main.container.big");
        Console.WriteLine("Selector Test:");
        Console.WriteLine($"Tag: {selectorTest.TagName}");
        Console.WriteLine($"Id: {selectorTest.Id}");
        Console.WriteLine($"Classes: {string.Join(", ", selectorTest.Classes)}");
        Console.WriteLine("-------------------------------------------");

        HtmlQueryService query = new();
        var results = query.Query(root, "div p span.item");
        Console.WriteLine($"Matched Elements Count: {results.Count}");

        foreach (var el in results)
        {
            Console.WriteLine($"<{el.Name}>  | InnerHtml: '{el.InnerHtml}'");
            Console.WriteLine("Ancestors: " + string.Join(" > ", el.Ancestors().Select(a => a.Name)));
            Console.WriteLine("------------------------------");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task<string> LoadHtmlFromUrl(string url)
    {
        using HttpClient client = new();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
