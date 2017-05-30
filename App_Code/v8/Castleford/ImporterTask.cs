using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;   
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Web;

using CMS;
using CMS.CustomTables;
using CMS.DataEngine;
using CMS.DocumentEngine;
using CMS.Helpers;
using CMS.Localization;
using CMS.Membership;
using CMS.Scheduler;
using CMS.SiteProvider;
using CMS.Taxonomy;
using CMS.WorkflowEngine;

using CastlefordImporterHelpers;

[assembly: RegisterCustomClass("CastlefordImporter", typeof(ImporterTask))]
public class ImporterTask : ITask
{
    private string settingsPrefix = "CFD";
    private string generalSettingsCodename = "castleford";
    private string mappingSettingsCodename = "castleford.mappings";
    private Dictionary<string, string> settings;
    private Dictionary<string, string> mappings;

    private TreeNode importTarget;
    private TreeProvider tree;

    private string importRecordTable = "castleford.imports";
    
    public string Execute(TaskInfo task)
    {
        // // 1. import all settings and dataField mappings
        this.settings = KenticoHelper.LoadSettingsKeys(generalSettingsCodename, this.settingsPrefix);
        this.mappings = KenticoHelper.LoadSettingsKeys(mappingSettingsCodename, this.settingsPrefix);

        if (settings == null || settings.Count == 0)
        {
            return KenticoLogger.ExitTask(string.Format("No settings data found. Failed to import settings group '{0}'.", generalSettingsCodename));
        }

        if (mappings == null || mappings.Count == 0)
        {
            return KenticoLogger.ExitTask(string.Format("No mapping data found. Failed to import settings group '{0}'.", mappingSettingsCodename));
        }

        // // 2. Check that importingRecordTable exists
        DataClassInfo customTableClassInfo = DataClassInfoProvider.GetDataClassInfo(importRecordTable);

        if (customTableClassInfo == null)
        {
            return KenticoLogger.ExitTask(String.Format("Custom table '{0}' not found", importRecordTable));
        }

        // // 3. Check that target Kentico document type exists
        DataClassInfo importTypeClassInfo = DataClassInfoProvider.GetDataClassInfo(settings["ImportDocumentType"]);

        if (importTypeClassInfo == null)
        {
            return KenticoLogger.ExitTask(String.Format("Import type '{0}' not found", settings["ImportDocumentType"]));
        }

        // // 4. Check that target import location exists in site tree
        this.tree = new TreeProvider(MembershipContext.AuthenticatedUser);
        this.importTarget = tree.SelectSingleNode(
            Guid.Parse(settings["ImportTargetLocation"]),
            LocalizationContext.CurrentCulture.CultureAlias,
            SiteContext.CurrentSiteName,
            true
        );

        if (this.importTarget == null)
        {
            return KenticoLogger.ExitTask(String.Format("Target location '{0}' not found", settings["ImportTargetLocation"]));
        }

        // // 5. Pull feed and import new
        int result = PollNewsFeed();
        // log success
        
        return string.Format("Imported {0} new articles on last run.", result);
    }    

    protected int PollNewsFeed()
    {
        List<string> priorIDs = GetAlreadyImportedArticles();
        List<XElement> newArticles = CheckFeedForNew(priorIDs);
        int numberImported = 0;

        foreach (XElement article in newArticles)
        {
            // Creating our host kentico document
            TreeNode kenticoArticle = TreeNode.New(settings["ImportDocumentType"], this.tree);
            kenticoArticle.DocumentCulture = LocalizationContext.CurrentCulture.CultureCode;

            #region
            foreach (string apiField in mappings.Keys)
            {
                string kenticoField = "";

                if (mappings[apiField] == null)
                {
                    // no mapping for this field, user doesn't want to import it
                    continue;
                }
                else
                {
                    kenticoField = mappings[apiField].ToString();
                }

                XElement apiValue = article.Element(apiField);

                if (apiValue == null)
                {
                    // newsitem from api does not contain field, skip
                    continue;
                }

                string value = apiValue.Value;

                if (apiField.Contains("Date"))
                {
                    // Fields with 'date' in the name will be stored as DateTime objects inside the
                    // host document type. Doing validation/conversion here.
                    try
                    {
                        kenticoArticle.SetValue(kenticoField, DateTime.Parse(value));
                    }
                    catch (Exception e)
                    {
                        KenticoLogger.LogInfo(string.Format("Invalid date format ({0}) in newsItem {1}\r\n\r\n{2}", value, article.Element("id").Value, e.ToString()));
                    }
                }
                else if (apiField == "teaserImage")
                {
                    // Teaser images are created from the lists of photo attachments that optionally
                    // belong to an article. We handle this field when we process photo attachments
                    // later on.
                    continue;
                }
                else
                {
                    // All other fields are plain/html text and can be copied to the host document 
                    // type without modification. 
                    kenticoArticle.SetValue(kenticoField, value);
                }
            }
            #endregion

            // set page template
            var pageTemplateInfo = CMS.PortalEngine.PageTemplateInfoProvider.GetPageTemplateInfo(settings["PageTemplate"]);

            if (pageTemplateInfo != null)
            {
                kenticoArticle.DocumentPageTemplateID = pageTemplateInfo.PageTemplateId;
            }

            bool insertSuccessful = false;

            if (settings["ImportDocumentType"] == "CMS.BlogPost")
            {
                insertSuccessful = KenticoHelper.InsertBlogIntoTree(kenticoArticle, this.importTarget, bool.Parse(settings["AllowBlogComments"]));
            }
            else
            {
                insertSuccessful = KenticoHelper.InsertIntoTree(kenticoArticle, this.importTarget, settings["ImportDocumentType"]);
            }

            if (!insertSuccessful)
            {
                continue;
            }

            // handle attachments and set teaser image
            string photosFeed = article.Element("photos").Attribute("href").Value ?? "";

            if (photosFeed != "")
            {
                DownloadPhotoContent(kenticoArticle, photosFeed);
            }

            // set document tags
            if (bool.Parse(settings["UseTags"]) && article.Element("tags") != null)
            {
                KenticoHelper.SetDocumentTags(kenticoArticle, article.Element("tags").Value.Split(','));
            }

            // set document categories
            XElement catURL = article.Element("categories");

            if (catURL != null && catURL.Attribute("href") != null)
            {
                SetDocumentCategories(kenticoArticle, catURL.Attribute("href").Value);
            }

            PublishWithWorkflow(kenticoArticle);

            DateTime lastPublished = DateTime.Parse(article.Element("lastModifiedDate").Value);
            LogArticleImport(kenticoArticle.NodeGUID, article.Element("id").Value, lastPublished);

            numberImported++;
        }

        return numberImported;
    }

    protected void SetDocumentCategories(TreeNode kenticoArticle, string catURL)
    {
        if (bool.Parse(settings["UseCategories"]) && catURL != "")
        {
            Dictionary<string,string> categories = LoadCategoryDefinitions(catURL);

            foreach (var category in categories)
            {
                try
                {
                    //int id = int.Parse(category.Value);
                    string categoryDisplayName = category.Key;
                    string categoryCodeName = categoryDisplayName.Replace(" ", "");

                    CategoryInfo cat = CategoryInfoProvider.GetCategoryInfo(categoryCodeName, SiteContext.CurrentSiteName);

                    if (cat == null)
                    {
                        cat = new CategoryInfo();

                        //cat.CategoryID = id;
                        cat.CategoryDisplayName = categoryDisplayName;
                        cat.CategoryName = categoryCodeName;
                        cat.CategoryDescription = "Category imported from Brafton API";
                        cat.CategorySiteID = SiteContext.CurrentSiteID;
                        cat.CategoryCount = 0;
                        cat.CategoryEnabled = true;

                        CategoryInfoProvider.SetCategoryInfo(cat);

                        // refetch to get fully fleshed-out object
                        cat = CategoryInfoProvider.GetCategoryInfo(categoryCodeName, SiteContext.CurrentSiteName);
                    }

                    string test = kenticoArticle.DocumentID.ToString();
                    string test2 = cat.ToString();

                    DocumentCategoryInfoProvider.AddDocumentToCategory(kenticoArticle.DocumentID, cat.CategoryID);
                }
                catch (Exception e)
                {
                    KenticoLogger.LogInfo(string.Format("Could not assign category {0}", category));
                    KenticoLogger.LogError(e.ToString());
                }
            }
        }
    }

    protected Dictionary<string, string> LoadCategoryDefinitions(string catURL)
    {
        Dictionary<string, string> results = new Dictionary<string, string>();
        XElement definitions = XElement.Load(catURL);

        if (definitions != null)
        {
            List<XElement> categories = definitions.Elements("category").ToList();

            foreach (XElement category in categories)
            {
                string name = category.Element("name").Value;
                string id = category.Element("id").Value;
                results.Add(name, id);
            }
        }

        return results;
    }

    protected void PublishWithWorkflow(TreeNode node)
    {
        WorkflowManager workflowManager = WorkflowManager.GetInstance(this.tree);
        WorkflowInfo workflow = workflowManager.GetNodeWorkflow(node);

        if (workflow != null)
        {
            workflowManager.PublishDocument(node, null);
        }
    }

    protected void DownloadPhotoContent(TreeNode article, string photosFeed)
    {
        XElement photoFeed = XElement.Load(photosFeed);

        IEnumerable<XElement> photos =
            from el in photoFeed.Elements("photo")
            where el.Element("id").Value != ""
            select el;

        String postedFile = "";

        for (int i = 0; i < photos.Count(); i++)
        {
            XElement photo = photos.ElementAt(i);

            string imageTitle, caption, htmlAlt, id, url, extension;
            imageTitle = caption = htmlAlt = id = url = extension = "";

            try
            {
                // process metadata for image
                caption = (photo.Element("caption") != null) ? photo.Element("caption").Value : "";
                htmlAlt = (photo.Element("htmlAlt") != null) ? photo.Element("htmlAlt").Value : "";
                id = (photo.Element("id") != null) ? photo.Element("id").Value : "";

                imageTitle = caption ?? htmlAlt ?? id ?? "Error: no image data";

                // select largest instance size to save
                IEnumerable<XElement> sizes =
                    from el in photoFeed.Element("photo").Element("instances").Elements("instance")
                    orderby int.Parse(el.Element("width").Value) descending
                    select el;

                XElement largest = sizes.First();

                url = largest.Element("url").Value;
                extension = url.Split('.').Last();

                var request = (HttpWebRequest)WebRequest.Create(url);
                var response = (HttpWebResponse)request.GetResponse();

                Image image = Image.FromStream(response.GetResponseStream());
                postedFile = HttpContext.Current.Server.MapPath(String.Format("~/App_Themes/{0}.{1}", RemoveIllegalCharacters(imageTitle), extension));
                image.Save(postedFile);

                DocumentHelper.AddUnsortedAttachment(article, Guid.NewGuid(), postedFile, this.tree, ImageHelper.AUTOSIZE, ImageHelper.AUTOSIZE, ImageHelper.AUTOSIZE);

                if (bool.Parse(settings["CreateTeaserImage"]) && mappings["teaserImage"] != "")
                {
                    DocumentHelper.AddAttachment(article, mappings["teaserImage"], postedFile, this.tree, ImageHelper.AUTOSIZE, ImageHelper.AUTOSIZE, ImageHelper.AUTOSIZE);
                }

                //File.Delete(postedFile);
                article.Update();
            }
            catch (Exception e)
            {
                KenticoLogger.LogInfo(string.Format("Could not download the following image: {0}", url));
                KenticoLogger.LogError(e.ToString());
            }
        }
    }

    protected void LogArticleImport(Guid kenticoID, string braftonID, DateTime lastModified)
    {
        CustomTableItem existing = CustomTableItemProvider.GetItems(this.importRecordTable)
                                        .WhereEquals("BraftonNewsID", braftonID)
                                        .FirstOrDefault();

        if (existing == null)
        {
            CustomTableItem newCustomTableItem = CustomTableItem.New(importRecordTable);

            newCustomTableItem.SetValue("KenticoNodeID", kenticoID);
            newCustomTableItem.SetValue("BraftonNewsID", braftonID);
            newCustomTableItem.SetValue("LastModifiedDate", lastModified);

            newCustomTableItem.Insert();
        }
        else
        {
            existing.SetValue("KenticoNodeID", kenticoID);
            existing.SetValue("BraftonNewsID", braftonID);
            existing.SetValue("LastModifiedDate", lastModified);

            existing.Update();
        }
    }
    
    protected List<string> GetAlreadyImportedArticles()
    {
        DataSet dataSet = CustomTableItemProvider.GetItems(this.importRecordTable);
        List<string> result = new List<string>();

        foreach (DataRow row in dataSet.Tables[0].Rows)
        {
            result.Add(row["BraftonNewsID"].ToString());
        }

        return result;
    }

    protected List<XElement> CheckFeedForNew(List<string> alreadyImportedIDs)
    {
        try
        {
            XElement root = XElement.Load(settings["ApiEndPoint"]);
            string apiNewsFeed = root.Element("news").Attribute("href").Value;

            XElement newsFeed = XElement.Load(apiNewsFeed);

            // queue all completely new articles for loading
            IEnumerable<XElement> newArticles =
                from el in newsFeed.Elements("newsListItem")
                where !alreadyImportedIDs.Contains(el.Element("id").Value)
                select el;

            List<XElement> results = new List<XElement>();

            foreach (XElement article in newArticles)
            {
                try
                {
                    results.Add(XElement.Load(article.Attribute("href").Value));
                }
                catch (Exception e)
                {
                    string href = (article.Attribute("href") != null) ? article.Attribute("href").Value : "noHref";
                    KenticoLogger.LogError(String.Format("Could not load article: {0}\r\n\r\n{1}", href, e.ToString()));
                }
            }

            // delete all update articles and queue them for reloading
            IEnumerable<XElement> updatedArticles = FindAndRemoveArticles(newsFeed.Elements("newsListItem"));

            foreach (XElement article in updatedArticles)
            {
                try
                {
                    results.Add(XElement.Load(article.Attribute("href").Value));
                }
                catch (Exception e)
                {
                    string href = (article.Attribute("href") != null) ? article.Attribute("href").Value : "noHref";
                    KenticoLogger.LogInfo(String.Format("Could not reload updated article: {0}. Original article was deleted!\r\n\r\n{1}", href, e.ToString()));
                    KenticoLogger.LogError(e.ToString());
                }
            }

            return results;
        }
        catch(NullReferenceException e)
        {
            KenticoLogger.LogError("Could not load Castleford news feed.");
            return new List<XElement>();
        }
    }

    protected List<XElement> FindAndRemoveArticles(IEnumerable<XElement> feed)
    {
        List<XElement> result = new List<XElement>();

        DataSet importRecords = CustomTableItemProvider.GetItems(this.importRecordTable);
        var customTable = from DataRow row in importRecords.Tables[0].Rows
                          select row;

        foreach (XElement article in feed)
        {
            string articleID = article.Element("id").Value;
            var customTableItem = customTable.FirstOrDefault(x => x["BraftonNewsID"].ToString() == articleID);

            if (customTableItem == null)
            {
                continue;
            }

            string braftonID = customTableItem["BraftonNewsID"].ToString();
            string kenticoGUID = customTableItem["KenticoNodeID"].ToString();

            try
            {
                DateTime apiLastModified = DateTime.Parse(article.Element("lastModifiedDate").Value);
                DateTime kenticoLastModified = DateTime.Parse(customTableItem["LastModifiedDate"].ToString()); 

                if (apiLastModified > kenticoLastModified)
                {
                    TreeNode node = tree.SelectSingleNode(
                        Guid.Parse(kenticoGUID),
                        LocalizationContext.CurrentCulture.CultureAlias,
                        SiteContext.CurrentSiteName,
                        true
                    );

                    if (node != null)
                    {
                        node.DeleteAllCultures();
                        result.Add(article);
                    }
                    else
                    {
                        throw new NullReferenceException(string.Format("Could not find imported article with brafton/kentico ids of {0}/{1}. The castleford.imports table is out of sync.", braftonID, kenticoGUID));
                    }
                }
            }
            catch (Exception e)
            {
                KenticoLogger.LogInfo(string.Format("Could not update + remove existing article (api ID {0})", braftonID));
                KenticoLogger.LogError(e.ToString());
            }
        }

        return result;
    }

    private string RemoveIllegalCharacters(string fileName)
    {
        string result = fileName.ToLower().Replace(' ', '-');
        Regex regex = new Regex(@"[^\w\-]");
        return regex.Replace(result, "");
    }
}