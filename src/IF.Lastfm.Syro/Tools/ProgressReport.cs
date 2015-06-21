﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using HtmlAgilityPack;
using System.Globalization;

namespace IF.Lastfm.Syro.Tools
{
    /// <summary>
    /// Scrapes Last.fm/api for available methods, compares to what commands are in IF.Lastfm.Core and generates some Markdown.
    /// Maybe some images if I'm feeling adventurous
    /// </summary>
    public class ProgressReport
    {
        public const string API_INTRO_PAGE = "http://www.last.fm/api/intro";

        private const string _progressReportIntro = @"# Api Progress ![Progress](http://progressed.io/bar/{0})

These are all the Last.fm API methods currently available. 

- Methods implemented by the [Inflatable Last.fm .NET SDK](https://github.com/inflatablefriends/lastfm) link to the relevant documentation page.
- Methods ~~marked with strikethrough~~ aren't currently implemented. Pull requests are welcome!
- Methods _marked with an asterisk *_ aren't listed on [the Last.fm documentation](http://www.last.fm/api), so they might not work!

This list is generated by the [ProgressReport](src/IF.Lastfm.ProgressReport) tool in the solution. Last updated on {1}
";
        
        #region Scrape

        /// <summary>
        /// Scrape the API documentation for all the method names
        /// </summary>
        internal static Dictionary<string, IEnumerable<string>> GetApiMethods()
        {
            var client = new HttpClient();
            var response = client.GetAsync(API_INTRO_PAGE);
            response.Wait();

            if (!response.Result.IsSuccessStatusCode)
            {
                Console.WriteLine("Server returned {0} fetching {1}\n{2}", 
                    response.Result.StatusCode, API_INTRO_PAGE, response.Result.ReasonPhrase);
                Console.ReadLine();
                return null;
            }

            var htmlTask = response.Result.Content.ReadAsStringAsync();
            htmlTask.Wait();

            var doc = new HtmlDocument();
            doc.LoadHtml(htmlTask.Result);

            var wspanel = doc.DocumentNode.Descendants().FirstOrDefault(d => d.GetAttributeValue("class", "").Contains("wspanel")
                && d.PreviousSibling.PreviousSibling.OuterHtml == "<h2>API Methods</h2>");
            if (wspanel == null)
            {
                throw new FormatException(string.Format("Couldn't find wspanel in HTML {0}", htmlTask.Result));
            }

            // each package is a section of the API
            var packages = wspanel.Descendants().Where(li => HasClass(li, "package"));

            var allMethods = new Dictionary<string, IEnumerable<string>>();
            foreach (var package in packages)
            {
                var h3 = package.Element("h3");

                var ul = package.Element("ul");

                var methodLinks = ul.Elements("li");
                var methods = methodLinks.Select(a => a.InnerText);

                allMethods.Add(h3.InnerText, methods);
            }

            return allMethods;
        }

        private static bool HasClass(HtmlNode stay, string classy)
        {
            return stay.Attributes.Contains("class") && stay.Attributes["class"].Value.Contains(classy);
        }

        #endregion
        
        #region Report

        internal static void WriteReport(Dictionary<string, IEnumerable<string>> apiGroup, List<string> allImplemented, string path)
        {
            var markdownBuilder = new StringBuilder();
            var percent = GetPercentage(apiGroup, allImplemented);
            markdownBuilder.AppendFormat(_progressReportIntro, (int)Math.Floor(percent), DateTime.UtcNow.ToString("f", CultureInfo.InvariantCulture));

            foreach (var group in apiGroup.OrderBy(kv => kv.Key))
            {
                var apiGroupName = group.Key;
                var implemented = allImplemented.Where(m => m.StartsWith(apiGroupName.ToLowerInvariant(), StringComparison.Ordinal)).ToList();

                var matches = group.Value.Intersect(implemented).ToList();
                var notImplemented = group.Value.Except(implemented).ToList();
                var secret = implemented.Except(group.Value).ToList();

                markdownBuilder.AppendFormat("## {0}\n\n", apiGroupName);
                foreach (var match in matches)
                {
                    markdownBuilder.AppendFormat("- [{0}](http://www.last.fm/api/show/{0})\n", match);
                }
                foreach (var match in notImplemented)
                {
                    markdownBuilder.AppendFormat("- ~~[{0}](http://www.last.fm/api/show/{0})~~\n", match);
                }
                foreach (var match in secret)
                {
                    markdownBuilder.AppendFormat("- _{0}_ *\n", match);
                }
                markdownBuilder.AppendLine();
            }

            var markdown = markdownBuilder.ToString();

            // write to output directory
            using (var fs = new FileStream(path, FileMode.Create))
            {
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(markdown);
                }
            }
        }

        public static double GetPercentage(Dictionary<string, IEnumerable<string>> apiGroup, List<string> allImplemented)
        {
            var percent = (((double) allImplemented.Count)/apiGroup.SelectMany(api => api.Value).Count())*100;
            return percent;
        }

        #endregion
    }
}
