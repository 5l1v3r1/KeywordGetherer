﻿using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KeywordGetherer
{
    class KeywordGetherer : DBConection
    {
        const string SETTINGS_SECTION = "keywords_getherer";

        private static Mutex kwMutext = new Mutex();
        protected int STEP;
        protected long offset;
        protected int limit;
        protected long file_offset;
        protected bool loadFromFile;
        protected string loadFromPath;
        protected IniFiles settings;


        public KeywordGetherer(int step=5)
        {
            this.STEP = step;
            this.init();
        }

        private void init()
        {
            this.clearCSV();

            this.offset = 0;
            this.limit = STEP;
            this.file_offset = 0;
            this.loadFromFile = false;
            this.loadFromPath = "";

            settings = new IniFiles(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)+"/Settings.ini");


            this.limit = !settings.KeyExists("limit", SETTINGS_SECTION) ?
               Int32.Parse(settings.Write("limit", "" + STEP, SETTINGS_SECTION)) :
               Int32.Parse(settings.Read("limit", SETTINGS_SECTION));

            this.loadFromFile = !settings.KeyExists("loadFromFile", SETTINGS_SECTION) ?
              Boolean.Parse(settings.Write("loadFromFile", "false", SETTINGS_SECTION)) :
              Boolean.Parse(settings.Read("loadFromFile", SETTINGS_SECTION));

            this.loadFromPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/" 
                + (!settings.KeyExists("loadFromPath", SETTINGS_SECTION) ?
                settings.Write("loadFromPath", "words.txt", SETTINGS_SECTION) :
                settings.Read("loadFromPath", SETTINGS_SECTION));

            if (loadFromFile)
                this.offset = !settings.KeyExists("file_offset", SETTINGS_SECTION) ?
                    long.Parse(settings.Write("file_offset", "" + file_offset, SETTINGS_SECTION)) :
                    long.Parse(settings.Read("file_offset", SETTINGS_SECTION));
            else
                this.offset = !settings.KeyExists("offset", SETTINGS_SECTION) ?
                   long.Parse(settings.Write("offset", "" + offset, SETTINGS_SECTION)) :
                   long.Parse(settings.Read("offset", SETTINGS_SECTION));
        }


        private void clearCSV()
        {
            Directory.GetFiles(Directory.GetCurrentDirectory()).
                ToList().
                ForEach(file =>
                {
                    if (file.IndexOf(".csv") != -1)
                        File.Delete(file);
                });
        }

        public async void execute()
        {
            Console.WriteLine("Запуск процесса зборщика слова");
            List<Task> taskList = new List<Task>();

            long count = this.fileDataCount();

            while (true)
            {
                kwMutext.WaitOne();
                var globalOffset = this.offset;
                if (loadFromFile)
                    globalOffset = !settings.KeyExists("file_offset", SETTINGS_SECTION) ?
                        long.Parse(settings.Write("file_offset", "" + file_offset, SETTINGS_SECTION)) :
                        long.Parse(settings.Read("file_offset", SETTINGS_SECTION));
                else
                    globalOffset = !settings.KeyExists("offset", SETTINGS_SECTION) ?
                       long.Parse(settings.Write("offset", "" + offset, SETTINGS_SECTION)) :
                       long.Parse(settings.Read("offset", SETTINGS_SECTION));

                this.offset = Math.Max(globalOffset, this.offset);

                this.limit = (STEP - Math.Min(taskList.Count, STEP));
                this.offset += (STEP - Math.Min(taskList.Count, STEP));
               

                settings.Write(this.loadFromFile ? "file_offset" : "offset", "" +this.offset, SETTINGS_SECTION);

                List<DBKeyword> list = loadFromFile ? loadFile(offset, limit) : this.listKeywords(offset, limit);
                if (list == null)
                {
                    kwMutext.ReleaseMutex();
                    break;
                }

                kwMutext.ReleaseMutex();
                list.ForEach(kw =>
                {                    
                    taskList.Add(Task.Run(() =>
                    {
                        this.takeKW(kw);
                       
                    }));
                    Thread.Sleep(YandexUtils.rndSleep());
                });
                Console.WriteLine("Задач в таске:" + taskList.Count());


                Task.WaitAny(taskList.ToArray());
                try
                {
                    taskList
                        .Where(t => t.IsCompleted)
                        .ToList()
                        .ForEach(t => {
                            taskList.Remove(t);
                           
                        });
                }
                catch {
                  
                }



                if (loadFromFile && offset >= count)
                {
                    settings.Write("loadFromFile", "false", SETTINGS_SECTION);
                    offset = long.Parse(settings.Read("offset", SETTINGS_SECTION));
                    loadFromFile = false;
                    Console.WriteLine("Переключаемся на выборку из БД");
                    Thread.Sleep(YandexUtils.rndSleep());
                }
                Thread.Sleep(YandexUtils.rndSleep());
                Console.WriteLine("offset=>" + offset + " count=>" + count);
            }

        }

        private async void takeKW(DBKeyword kw, Boolean useSlicer = true)
        {

            ChromeDriver driver = this.initDriver(kw);
            if (!driver.isSelectorExist(By.CssSelector(".report-download-button")))
            {
                driver.Close();
                return;
            }

            IWebElement element = driver.FindElement(By.CssSelector(".report-download-button"));
            string fileName = element.GetAttribute("download");
            Console.WriteLine("Скачиваем:" + fileName);
            element.SendKeys(Keys.Enter);

            while (!File.Exists(fileName))
                Thread.Sleep(YandexUtils.rndSleep());
            driver.Close();
            try
            {
                File.ReadLines(fileName)
                    .Skip(1)
                    .ToList()
                    .ForEach(s =>
                    {
                        string[] buf = s.Split(';');
                        string keyword = (new Regex("['\"]")).Replace(buf[0], "");

                        if (int.Parse(buf[1]) <= 7 && int.Parse(buf[4]) <= 100)
                        {
                            if (!this.isKeywordExist(keyword))
                                this.Insert(keyword);

                            if (useSlicer)
                                keyword.sliceAndTakeVariants();
                        }

                    });
            }
            catch (Exception e)
            {
                Console.WriteLine("Мы тут упали:" + e.Message);
            }
            File.Delete(fileName);

        }

        public long fileDataCount()
        {
            if (!File.Exists(this.loadFromPath))
                throw new FileNotFoundException(this.loadFromPath);
            try
            {
                return (long)File.ReadAllLines(this.loadFromPath).ToList().Count();
            }
            catch
            {
                return 0;
            }
        }
        public List<DBKeyword> loadFile(long offset, int limit)
        {
            if (!File.Exists(this.loadFromPath))
                throw new FileNotFoundException(this.loadFromPath);

            List<DBKeyword> list = new List<DBKeyword>();
            File.ReadLines(this.loadFromPath)
                .Skip((int)offset)
                .Take(limit)
                .ToList()
                .ForEach(s =>
                {
                    DBKeyword dbw = new DBKeyword();
                    dbw.keyword = s;
                    list.Add(dbw);
                });

            return list;
        }

        private ChromeDriver initDriver(DBKeyword kw)
        {
            var options = new ChromeOptions();
            //options.AddArgument("no-sandbox");
            options.AddUserProfilePreference("download.default_directory", Directory.GetCurrentDirectory());
            options.AddArguments("--disable-extensions");
            // options.AddArgument("no-sandbox");
            options.AddArgument("--incognito");
            //options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");  //--d

            kwMutext.WaitOne();
            ChromeDriver driver = new ChromeDriver(options);//открываем сам браузер
            driver.LocationContext.PhysicalLocation = new OpenQA.Selenium.Html5.Location(55.751244, 37.618423, 152);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10); //время ожидания компонента страницы после загрузки страницы
            driver.Manage().Cookies.DeleteAllCookies();
            kwMutext.ReleaseMutex();

            driver.Navigate().GoToUrl("http://www.bukvarix.com/keywords/?q=" + kw.keyword + "&r=report");

            return driver;
        }

    }
}
