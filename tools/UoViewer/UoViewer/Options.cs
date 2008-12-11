/***************************************************************************
 *
 * $Author: Turley
 * 
 * "THE BEER-WARE LICENSE"
 * As long as you retain this notice you can do whatever you want with 
 * this stuff. If we meet some day, and you think this stuff is worth it,
 * you can buy me a beer in return.
 *
 ***************************************************************************/

using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Ultima;
using System.Windows.Forms;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace UoViewer
{
    public class Options 
    {
        private static bool m_UpdateCheckOnStart = false;
        /// <summary>
        /// Definies if an Update Check should be made on startup
        /// </summary>
        public static bool UpdateCheckOnStart
        {
            get { return m_UpdateCheckOnStart; }
            set { m_UpdateCheckOnStart = value; }
        }

        public Options() 
        {
            Load();
            if (m_UpdateCheckOnStart)
            {
                BackgroundWorker updater = new BackgroundWorker();
                updater.DoWork += new DoWorkEventHandler(Updater_DoWork);
                updater.RunWorkerCompleted+=new RunWorkerCompletedEventHandler(Updater_RunWorkerCompleted);
                updater.RunWorkerAsync();
            }
        }

        public static void Save()
        {
            string filepath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;

            string FileName = Path.Combine(filepath, "Options.xml");

            XmlDocument dom = new XmlDocument();
            XmlDeclaration decl = dom.CreateXmlDeclaration("1.0", "utf-8", null);
            dom.AppendChild(decl);
            XmlElement sr = dom.CreateElement("Options");

            XmlComment comment= dom.CreateComment("ItemSize controls the size of images in items tab");
            sr.AppendChild(comment);
            XmlElement elem = dom.CreateElement("ItemSize");
            elem.SetAttribute("width", Controls.Options.ArtItemSizeWidth.ToString());
            elem.SetAttribute("height", Controls.Options.ArtItemSizeHeight.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("ItemClip images in items tab shrinked or clipped");
            sr.AppendChild(comment);
            elem = dom.CreateElement("ItemClip");
            elem.SetAttribute("active", Controls.Options.ArtItemClip.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("CacheData should mul entries be cached for faster load");
            sr.AppendChild(comment);
            elem = dom.CreateElement("CacheData");
            elem.SetAttribute("active", Files.CacheData.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("NewMapSize Felucca/Trammel width 7168?");
            sr.AppendChild(comment);
            elem = dom.CreateElement("NewMapSize");
            elem.SetAttribute("active", Ultima.Map.Felucca.Width==7168 ? true.ToString() : false.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("Alternative layout in items tab?");
            sr.AppendChild(comment);
            elem = dom.CreateElement("AlternativeDesign");
            elem.SetAttribute("active", Controls.Options.DesignAlternative.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("Use Hashfile to speed up load?");
            sr.AppendChild(comment);
            elem = dom.CreateElement("UseHashFile");
            elem.SetAttribute("active", Files.UseHashFile.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("Should an Update Check be done on startup?");
            sr.AppendChild(comment);
            elem = dom.CreateElement("UpdateCheck");
            elem.SetAttribute("active", UpdateCheckOnStart.ToString());
            sr.AppendChild(elem);
            comment = dom.CreateComment("Pathsettings");
            sr.AppendChild(comment);

            ArrayList sorter = new ArrayList(Files.MulPath.Keys);
            sorter.Sort();
            foreach (string key in sorter)
            {
                XmlElement path = dom.CreateElement("Paths");
                path.SetAttribute("key", key.ToString());
                path.SetAttribute("value", Files.MulPath[key].ToString());
                sr.AppendChild(path);
            }
            dom.AppendChild(sr);
            dom.Save(FileName);
        }

        private void Load()
        {
            string path = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            string FileName = Path.Combine(Path.GetDirectoryName(path), "Options.xml");
            if (!System.IO.File.Exists(FileName))
                return;

            XmlDocument dom = new XmlDocument();
            dom.Load(FileName);
            XmlElement xOptions = dom["Options"];

            XmlElement elem = (XmlElement)xOptions.SelectSingleNode("ItemSize");
            if (elem != null)
            {
                Controls.Options.ArtItemSizeWidth = int.Parse(elem.GetAttribute("width"));
                Controls.Options.ArtItemSizeHeight = int.Parse(elem.GetAttribute("height"));
            }
            elem = (XmlElement)xOptions.SelectSingleNode("ItemClip");
            if (elem != null)
                Controls.Options.ArtItemClip = bool.Parse(elem.GetAttribute("active"));

            elem = (XmlElement)xOptions.SelectSingleNode("CacheData");
            if (elem != null)
                Files.CacheData = bool.Parse(elem.GetAttribute("active"));

            elem = (XmlElement)xOptions.SelectSingleNode("NewMapSize");
            if (elem != null)
            {
                if (bool.Parse(elem.GetAttribute("active")))
                {
                    Ultima.Map.Felucca.Width = 7168;
                    Ultima.Map.Trammel.Width = 7168;
                }
            }

            elem = (XmlElement)xOptions.SelectSingleNode("AlternativeDesign");
            if (elem != null)
            {
                Controls.Options.DesignAlternative = bool.Parse(elem.GetAttribute("active"));
                UoViewer.AlternativeDesign = Controls.Options.DesignAlternative;
            }

            elem = (XmlElement)xOptions.SelectSingleNode("UseHashFile");
            if (elem != null)
                Files.UseHashFile = bool.Parse(elem.GetAttribute("active"));

            elem = (XmlElement)xOptions.SelectSingleNode("UpdateCheck");
            if (elem != null)
                UpdateCheckOnStart = bool.Parse(elem.GetAttribute("active"));

            foreach (XmlElement xPath in xOptions.SelectNodes("Paths"))
            {
                string key;
                string value;
                key = xPath.GetAttribute("key");
                value = xPath.GetAttribute("value");
                Files.MulPath[key] = value;
            }

            if (Files.GetFilePath("map1.mul") != null)
            {
                if (Ultima.Map.Trammel.Width == 7168)
                    Ultima.Map.Trammel = new Ultima.Map(1, 1, 7168, 4096);
                else
                    Ultima.Map.Trammel = new Ultima.Map(1, 1, 6144, 4096);
            }
        }

        /// <summary>
        /// Checks polserver forum for updates
        /// </summary>
        /// <returns></returns>
        public static Match CheckForUpdate(out string error)
        {
            StringBuilder sb = new StringBuilder();
            byte[] buf = new byte[8192];
            Match match;
            error = "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)
                    WebRequest.Create(@"http://forums.polserver.com/viewtopic.php?f=1&t=2351&st=0&sk=t&sd=a");

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Stream resStream = response.GetResponseStream();

                string tempString = null;
                int count = 0;

                do
                {
                    count = resStream.Read(buf, 0, buf.Length);
                    if (count != 0)
                    {
                        tempString = Encoding.ASCII.GetString(buf, 0, count);
                        sb.Append(tempString);
                    }
                }
                while (count > 0);

                Regex reg = new Regex(@"<a href=""./download/file.php\?id=(?<id>[\d]+)&amp;sid=(?<sid>[\w]+)"">UOViewer (?<major>\d).(?<minor>\d)(?<sub>\w)?.rar</a>", RegexOptions.Compiled);
                match = reg.Match(sb.ToString());
                response.Close();
                resStream.Dispose();
            }
            catch (Exception e)
            {
                match = null;
                error = e.Message;
            }

            return match;
        }

        private void Updater_DoWork(object sender, DoWorkEventArgs e)
        {
            string error;
            e.Result = CheckForUpdate(out error);
            if (e.Result == null)
                throw new Exception(error);

        }

        private void Updater_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error!=null)
            {
                MessageBox.Show("Error:\n" + e.Error, "Check for Update");
                return;
            }
            Match match = (Match)e.Result;
            if (match.Success)
            {
                string version = match.Result("${major}.${minor}${sub}");
                if (UoViewer.Version.Equals(version))
                    MessageBox.Show("Your Version is up-to-date", "Check for Update");
                else
                {
                    DialogResult result =
                        MessageBox.Show(String.Format(@"Your version differs: {0} Found: {1}"
                        , UoViewer.Version, version) + "\nDownload now?", "Check for Update", MessageBoxButtons.YesNo);
                    if (result == DialogResult.Yes)
                        DownloadFile(version, match.Result("${id}"));
                }
            }
            else
                MessageBox.Show("Failed to get Versioninfo", "Check for Update");
        }

        #region Downloader
        private void DownloadFile(string version, string id)
        {
            string filepath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            string FileName = Path.Combine(filepath, String.Format("UoViewer {0}.rar",version));

            WebClient web = new WebClient();
            web.DownloadFileCompleted += new AsyncCompletedEventHandler(OnDownloadFileCompleted);
            web.DownloadFileAsync(new Uri(String.Format(@"http://forums.polserver.com/download/file.php\?id={0}", id)), FileName);
        }

        private void OnDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show("An error occurred while downloading UOViewer\n" + e.Error.Message,
                    "Updater");
                return;
            }
            MessageBox.Show("Finished Download","Updater");
        }
        #endregion
    }
}