using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

namespace CastlefordImporterHelpers
{
    public static class KenticoHelper
    {
        public static Dictionary<string, string> LoadSettingsKeys(string settingsCategory, string settingsPrefix)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            // Get all settings groups in the target category
            SettingsCategoryInfo category = SettingsCategoryInfoProvider.GetSettingsCategoryInfoByName(settingsCategory);
            var groups = SettingsCategoryInfoProvider.GetChildSettingsCategories(category.CategoryID);

            // Get all keys in all settings groups
            foreach (var group in groups)
            {
                var keys = SettingsKeyInfoProvider.GetSettingsKeys(group.CategoryID).ToList();

                foreach (var key in keys)
                {
                    result.Add(key.KeyName.Replace(settingsPrefix, ""), key.KeyValue);
                }
            }

            return result;
        }

        public static bool InsertBlogIntoTree(TreeNode kenticoArticle, TreeNode importTarget, bool allowBlogComments)
        {
            TreeProvider tree = new TreeProvider(MembershipContext.AuthenticatedUser);

            try
            {
                // allow / deny blog comments based on settings
                kenticoArticle.SetValue("BlogPostAllowComments", allowBlogComments);

                // ensure that BlogPostSummary is not null
                kenticoArticle.SetValue("BlogPostSummary", kenticoArticle.GetValue("BlogPostSummary") ?? "");

                // put blog posts in the folder that Kentico creates for each month
                TreeNode blogMonth = FindExistingBlogMonth(kenticoArticle, importTarget.NodeAliasPath);

                if (blogMonth != null)
                {
                    blogMonth.DocumentCulture = LocalizationContext.CurrentCulture.CultureCode;
                    blogMonth.Update();

                    DocumentHelper.InsertDocument(kenticoArticle, blogMonth, tree);
                }
                else
                {
                    var blogTarget = DocumentHelper.EnsureBlogPostHierarchy(kenticoArticle, importTarget, tree);
                    //blogTarget.DocumentCulture = LocalizationContext.CurrentCulture.CultureCode;
                    //blogTarget.Update();

                    DocumentHelper.InsertDocument(kenticoArticle, blogTarget, tree);
                }
            }
            catch (Exception e)
            {
                KenticoLogger.LogInfo(string.Format("Could not insert blog post: {0}", kenticoArticle.DocumentName));
                KenticoLogger.LogError(e.ToString());
                return false;
            }

            return true;
        }

        public static bool InsertIntoTree(TreeNode kenticoArticle, TreeNode importTarget, string documentType)
        {
            TreeProvider tree = new TreeProvider(MembershipContext.AuthenticatedUser);

            try
            {
                DocumentHelper.InsertDocument(kenticoArticle, importTarget, tree);
            }
            catch (Exception e)
            {
                KenticoLogger.LogError(string.Format("Could not insert document: {0}\r\n\r\n{1}", kenticoArticle.NodeAliasPath, e.Message));
                return false;
            }

            return true;
        }

        private static TreeNode FindExistingBlogMonth(TreeNode kenticoArticle, string targetAliasPath)
        {
            DateTime publishDate = kenticoArticle.GetDateTimeValue("BlogPostDate", DateTime.Now);
            string monthName = String.Format("{0:MMMM} {0:yyyy}", publishDate);

            int siteID = SiteContext.CurrentSiteID;
            string cultureCode = LocalizationContext.CurrentCulture.CultureCode;

            TreeProvider tree = new TreeProvider(MembershipContext.AuthenticatedUser);

            //return tree.SelectNodes("CMS.BlogMonth").Where(
            //    x => x.NodeSiteID == siteID && 
            //         x.DocumentCulture == cultureCode && 
            //         x.DocumentName == monthName).First();

            return tree.SelectNodes("CMS.BlogMonth").Where(
                x => x.NodeAliasPath.IndexOf(targetAliasPath) == 0 &&
                     x.NodeSiteID == siteID &&
                     x.DocumentName == monthName).FirstOrDefault();
        }

        public static void SetDocumentTags(TreeNode kenticoArticle, string[] tags)
        {
            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    string tag = tags[i];
                    tags[i] = FormatTag(tag);
                }

                kenticoArticle.DocumentTags = tags.Join(",");
                kenticoArticle.Update();
            }
        }

        private static string FormatTag(string tag)
        {
            if (tag != "")
            {
                bool singleQuoted = tag.IndexOf('\'') == 0 && tag[tag.Length - 2] != '\'';
                bool doubleQuoted = tag.IndexOf('"') == 0 && tag[tag.Length - 2] != '"';

                if (singleQuoted)
                {
                    string sub = tag.Substring(1, tag.Length - 2);
                    tag = string.Format("\"{0}\"", sub);
                }
                else if (!doubleQuoted && tag.Contains(" "))
                {
                    tag = string.Format("\"{0}\"", tag);
                }
            }

            return tag;
        }
    }
}
