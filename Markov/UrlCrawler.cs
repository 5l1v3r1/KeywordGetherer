﻿
using MarkVSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KeywordGetherer.Markov
{
    class UrlCrawler : DBConection
    {
        const string SETTINGS_SECTION = "url_crawler";
        protected string working_dir = "markov_data";

        private static Mutex siteMutext = new Mutex();

        protected IniFiles settings;

        public int limit;
        public long offset;

        public UrlCrawler()
        {
            this.init();
        }

        public async void execute()
        {
            this.working_dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)+"/"+ this.working_dir;
            if (!Directory.Exists(working_dir))
                Directory.CreateDirectory(working_dir);

            while (true)
            {
                siteMutext.WaitOne();

                var globalOffset = !settings.KeyExists("offset", SETTINGS_SECTION) ?
                    long.Parse(settings.Write("offset", "" + offset, SETTINGS_SECTION)) :
                    long.Parse(settings.Read("offset", SETTINGS_SECTION));

                this.offset = Math.Max(this.offset, globalOffset);
                
                this.offset += this.limit;
                
                settings.Write("offset", "" + this.offset, SETTINGS_SECTION);

                siteMutext.ReleaseMutex();

                this.listSites(this.offset, this.limit)
                    .ToList().ForEach(link =>
                    {
                        FileStream file = null;
                        string path_link = link.Replace("/", "_");
                        string SiteUrl = @"http://" + link;
    
                        List<String> urlsInSite = new List<string>();

                        urlsInSite.Add(SiteUrl);

                        int index = 0;
                        

                        while (index < urlsInSite.Count)
                        {

                            string localUrl = urlsInSite.ToArray()[index];
                            Console.WriteLine("Берем [{0}] url", localUrl);
                            ///System.Threading.Thread.Sleep(5000);
                            WebRequest webr = null;
                            HttpWebResponse resp = null;

                            try
                            {
                                webr = WebRequest.Create(localUrl);
                                resp = (HttpWebResponse)webr.GetResponse();
                            }
                            catch (Exception ex)
                            {
                                index++;
                                continue;
                            }
                                                      

                            if (resp == null)
                            {
                                index++;
                                continue;
                            }
                            Stream stream = resp.GetResponseStream();

                            StreamReader sr = null;
                            try
                            {
                                sr = new StreamReader(stream, Encoding.GetEncoding(resp.CharacterSet));
                            }catch {
                                index++;
                                continue;
                            }

                            string sReadData = sr.ReadToEnd()
                                .Replace("\n", String.Empty)
                                .Replace("\t", String.Empty);

                            switch (resp.StatusCode)
                            {
                                case HttpStatusCode.OK:        //HTTP 200 - всё ОК
                                                               //

                                    Regex reg = new Regex("<a href=\"([^\"]*)\">");
                                    MatchCollection mc = reg.Matches(sReadData);
                                    foreach (Match m in mc)
                                    {
                                        string rez = m.Groups[1].Value.ToLower();

                                        Regex reg_img = new Regex("(jpg|png|gif|bmp|pdf|jpeg|doc|docx|rar|zip)");



                                        rez = rez.IndexOf(SiteUrl) != -1 ? rez : SiteUrl + rez;
                                        if (!Array.Exists(urlsInSite.ToArray(), element => element == rez)
                                            && reg_img.Matches(rez).Count == 0
                                            && rez.IndexOf(SiteUrl) != -1

                                            )
                                        {

                                            urlsInSite.Add(rez.IndexOf("http") == -1 ? SiteUrl + rez : rez);
                                            Console.WriteLine(rez.IndexOf("http") == -1 ? SiteUrl + rez : rez);
                                        }
                                    }

                                    reg = new Regex(@"<p[^>]*>\s*(.*?)\s*<\/p>");
                                    mc = reg.Matches(sReadData);
                                    StringBuilder stb = new StringBuilder();
                                    foreach (Match m in mc)
                                    {
                                        string rez = Regex
                                            .Replace(m.Groups[1].Value, "<[^>]+>", string.Empty);

                                        siteMutext.WaitOne();
                                         File.AppendAllText(working_dir + "/data[" + path_link + "].txt", WebUtility.HtmlDecode(rez));
                                     
                                        siteMutext.ReleaseMutex();

                                    }                                                                   
                                   
                                    break;

                                case HttpStatusCode.Forbidden: //HTTP 403 - доступ запрещён
                                    break;
                                case HttpStatusCode.NotFound:  //HTTP 404 - документ не найден
                                    break;
                                case HttpStatusCode.Moved:     //HTTP 301 - документ перемещён
                                    break;
                                default:                       //другие ошибки
                                    break;
                            }
                            index++;
                        }
                    });
            }
        }

        private void init(int step = 5)
        {
            this.offset = 0;
            this.limit = step;

            settings = new IniFiles(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/Settings.ini");

            this.limit = !settings.KeyExists("limit", SETTINGS_SECTION) ?
               Int32.Parse(settings.Write("limit", "" + this.limit, SETTINGS_SECTION)) :
               Int32.Parse(settings.Read("limit", SETTINGS_SECTION));

        }
    }
}
