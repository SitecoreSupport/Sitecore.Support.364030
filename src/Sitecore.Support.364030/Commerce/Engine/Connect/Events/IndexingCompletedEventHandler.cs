using Sitecore.Caching;
using Sitecore.Commerce.Engine.Connect.DataProvider;
using Sitecore.Commerce.Engine.Connect.Events;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.StringExtensions;
using Sitecore.Web;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;

namespace Sitecore.Support.Commerce.Engine.Connect.Events
{
    public class IndexingCompletedEventHandler : Sitecore.Commerce.Engine.Connect.Events.IndexingCompletedEventHandler
    {
        private int _cacheCleanThreshold = 10000;

        public IndexingCompletedEventHandler()
        {
            int threshold;
            if (int.TryParse(Sitecore.Configuration.Settings.GetSetting("IndexingCompletedCacheCleaningThreshold", "10000"), out threshold))
            {
                _cacheCleanThreshold = threshold;
            }
        }

        public override void OnIndexingCompleted(object sender, EventArgs e)
        {
            bool reloadMappings = false;

            IndexingCompletedEventArgs indexingCompletedEventArgs;
            if (e == null || (indexingCompletedEventArgs = (e as IndexingCompletedEventArgs)) == null)
            {
                return;
            }

            Database database = Factory.GetDatabase(indexingCompletedEventArgs.DatabaseName, assert: false);
            if (database == null)
            {
                return;
            }

            Log.Info("OnIndexingCompleted - Started for '" + database.Name + "'.", this);

            // clean caches partially based on configured threshold
            if (indexingCompletedEventArgs.SitecoreIds.Length <= _cacheCleanThreshold)
            {
                Log.Info("OnIndexingCompleted - Performing incremental cache updates.", this);
                string[] sitecoreIds = indexingCompletedEventArgs.SitecoreIds;
                foreach (string text in sitecoreIds)
                {
                    ID result;
                    if (!ID.TryParse(text, out result))
                    {
                        continue;
                    }

                    Item item = database.GetItem(result);
                    if (item != null)
                    {
                        Log.Info(string.Format("{0} - Removing '{1}' in database:{2} from caches.", "OnIndexingCompleted", result, database.Name), this);
                        CatalogRepository.DefaultCache.RemovePrefix(text);
                        database.Caches.ItemCache.RemoveItem(result);
                        database.Caches.DataCache.RemoveItemInformation(result);
                        database.Caches.StandardValuesCache.RemoveKeysContaining(result.ToString());
                        database.Caches.PathCache.RemoveKeysContaining(result.ToString());
                        database.Caches.ItemPathsCache.Remove(new ItemPathCacheKey(item.Paths.FullPath, result));
                        SiteInfo site = GetSite(item);
                        if (site != null)
                        {
                            Log.Info("Using Host name '" + site.HostName + "' with Site '" + site.Name + "' for HTML cache selective refresh", this);
                            HtmlCache htmlCache = site.HtmlCache;
                            if (htmlCache != null)
                            {
                                htmlCache.RemoveKeysContaining(item.Name);
                                htmlCache.RemoveKeysContaining(text);
                            }
                        }
                    }
                    else
                    {
                        Log.Info(string.Format("{0} - Found new item '{1}'.", "OnIndexingCompleted", result), this);
                        reloadMappings = true;
                    }
                }
                
                if (reloadMappings)
                {
                    Log.Info("OnIndexingCompleted - Updating mapping entries.", this);
                    new CatalogRepository().UpdateMappingEntries(DateTime.UtcNow);
                }
            }
            else
            {
                Log.Info($"OnIndexingCompleted - Performing full cache refresh. Number of changes: '{indexingCompletedEventArgs.SitecoreIds.Length}'", this);
                CacheManager.ClearAllCaches();
                
                Log.Info("OnIndexingCompleted - Updating mapping entries.", this);
                new CatalogRepository().UpdateMappingEntries(DateTime.UtcNow);
            }
        }

        private SiteInfo GetSite(Item item)
        {
            List<SiteInfo> siteInfoList = Factory.GetSiteInfoList();
            SiteInfo result = null;
            try
            {
                foreach (SiteInfo item2 in siteInfoList)
                {
                    string value = "{0}{1}".FormatWith(item2.RootPath, item2.StartItem);
                    if (!string.IsNullOrWhiteSpace(value) && item.Paths.FullPath.StartsWith(value, StringComparison.InvariantCultureIgnoreCase))
                    {
                        result = item2;
                        return result;
                    }
                }
                return result;
            }
            catch (Exception)
            {
                Log.Warn($"Indexing Complete Event Handler - Cannot get SiteInfo for item {item.Name}:{item.ID}", this);
                return result;
            }
        }
    }
}