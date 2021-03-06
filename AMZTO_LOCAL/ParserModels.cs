﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using HtmlAgilityPack;
using System.Net;
using System.Data;
using System.Linq;
using System.Threading;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support;
using System.Diagnostics;

namespace AMZTO_LOCAL
{
    public class parseProduct
    {
        #region declare Class Variables

        public static ChromeDriver driver;

        private String shtml;

        public static bool isStop = false;

        private UploadContext db = new UploadContext();

        public parseProduct(String html)
        {
            this.shtml = html;
        }

        public static parseProduct setHTML(String url)
        {
            //get URL and parse into HTML text
            String html = getStringFromUrl(url);
            parseProduct parser = new parseProduct(html);
            return parser;
        }

        public static String getPageURI(String base_url, int page)
        {
            if (page > 1)
            {
                String pure_link = base_url.Replace(".html", "");
                base_url = pure_link + "/" + page + ".html";
            }
            return base_url;
        }
        #endregion

        public static List<itemDataSet> getProduct(string URI)
        {
            Console.WriteLine("Start parsing product URL :: " + URI);

            List<itemDataSet> result;
            //string maxItem;
            int max_page;
            string sr_max_page;
            //Fetch first page
            parseProduct parser = parseProduct.setHTML(getPageURI(URI, 1));
            try
            {
                //Set local variable maxItem and MaxPage
                //maxItem = parser.getMaxItemNumber();
                sr_max_page = parser.getMaxPage();
                if (sr_max_page == null)
                {
                    Console.WriteLine("Can not get max page, skip parsing.");
                    return null;
                }
                max_page = Convert.ToInt32(parser.getMaxPage());
                Console.WriteLine("Product link " + URI + " Contains "+ max_page + " pages");
                result = parser.parseChildNode(URI);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Parse Products Error : " + ex.ToString());
                return null;
            }

            parseProduct tmp; // set temp page datasets
            List<itemDataSet> tmp_result;
            for (int i = 2; i <= max_page; i++)
            {
                //Parse item in each page
                tmp = parseProduct.setHTML(getPageURI(URI, i));
                tmp_result = tmp.parseChildNode(URI);

                if (tmp_result == null)
                {
                    //there is no pages left, break
                    max_page = i - 1;
                    break;
                }
                result.AddRange(tmp_result);
            }
            return result;
        }

        public List<itemDataSet> parseChildNode(String URI)
        {
            try
            {
                List<itemDataSet> items = new List<itemDataSet>();
                HtmlDocument document = new HtmlDocument();

                document.LoadHtml(shtml);
                //Read data grid
                HtmlNodeCollection productlist = document.DocumentNode.SelectNodes("//ul[@class='items-list util-clearfix']/li");
                if (productlist == null)
                {
                    Console.WriteLine("ProductLink for " + URI + " is null, Exit.");
                    return items;
                }
                int ProductLinkCount = productlist.Count;
                int currentProduct = 1;
                Console.WriteLine("ProductLink for " + URI + " is " + ProductLinkCount + ".");
                Console.WriteLine("parsing ProductLink number ");
                foreach (HtmlNode node in productlist)
                {
                    Console.Write("\r{0}   ", currentProduct);
                    itemDataSet item = ReadProductNode(node);
                    if (item.isStock == false)
                    {
                        Console.WriteLine("Out of stock", "Item is out of stock");
                        return null;
                    }
                    item.ParentID = "Parent";
                    item.ChildID = "";
                    item.Source = URI;
                    var query = (from p in db.LinkDataSet
                                 where p.sourceLink == URI
                                 orderby p.ShopName
                                 select p).FirstOrDefault();
                    item.ShopName = query.ShopName;
                    items.Add(item);
                    currentProduct++;
                }

                return items;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Parse Child Node Error : " + ex.ToString());
                return null;
            }
        }

        public itemDataSet Copy(itemDataSet Old)
        {
            itemDataSet itemTmp = new itemDataSet();
            itemTmp = Old;
            return itemTmp;
        }

        public itemDataSet ReadProductNode(HtmlNode gridNode)
        {
            itemDataSet _item = new itemDataSet();

            HtmlDocument product_node = new HtmlDocument();

            product_node.LoadHtml(gridNode.InnerHtml);
            try
            {
                _item.ProductName = product_node.DocumentNode.SelectSingleNode("//div[@class='detail']//a").Attributes["title"].Value;
                _item.ProductLink = product_node.DocumentNode.SelectSingleNode("//div[@class='detail']//a").Attributes["href"].Value;
                //_item.ProductID = product_node.DocumentNode.SelectSingleNode("//*[@id='base']/div[2]/div[2]/span").Attributes["value"].Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Read Product node Error : " + ex.ToString());
            }

            parseProduct.getItemInformation(_item.ProductLink, _item);

            return _item;
        }

        public static void getItemInformation(String url, itemDataSet _item)
        {
            String desc_page = getStringFromUrl(url);
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(desc_page);

            if (document.DocumentNode.SelectSingleNode("//*[@id='sku-active-no']") == null)
            {
                _item.isStock = false;
            }
            _item.isStock = true;
            try
            {
                //set main Image
                HtmlNode mainImage_node = document.DocumentNode.SelectSingleNode("//div[@id='magnifier']/div[1]/a/img");
                if (mainImage_node != null)
                {
                    string words = mainImage_node.Attributes["src"].Value;
                    int index = words.LastIndexOf("_");
                    if (index > 0)
                    {
                        words = words.Substring(0, index);
                    }
                    _item.ImageLink = words;
                }
                //get item descriptions
                HtmlNode desc_node = document.DocumentNode.SelectSingleNode("//div[@id='j-product-desc']");
                if (desc_node != null)
                    _item.Descriptions.AddRange(getDescription(desc_node.InnerHtml));

                //get size info
                HtmlNode desc_node2 = document.DocumentNode.SelectSingleNode("//div[@id='j-product-info-sku']");
                if (desc_node2 != null)
                    _item.Specifications.Add(getStyleAndSize(desc_node2.InnerHtml, _item));

                //get Price
                HtmlNode price_node = document.DocumentNode.SelectSingleNode("//*[@class='product-price-main']");
                if (price_node != null)
                    getPrice(price_node.InnerHtml, _item);

                //get small image
                HtmlNode image_nodes = document.DocumentNode.SelectSingleNode("//*[@id='j-image-thumb-list']");
                if (image_nodes != null)
                    _item.smallImage.AddRange(getSmallImage(image_nodes.InnerHtml, _item));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Parse Item Informations Error : " + ex.ToString());
            }
        }

        public static List<itemDescriptionsSet> getDescription(String html)
        {
            //0 = Features, 1 = spec
            List<itemDescriptionsSet> result = new List<itemDescriptionsSet>();
            itemDescriptionsSet itemResult = new itemDescriptionsSet();
            itemResult.descType = "";
            itemResult.descValue = "";
            result.Add(itemResult);          
            return result;
        }

        public static itemSpecificationsSet getStyleAndSize(String html, itemDataSet _item)
        {
            itemSpecificationsSet itemSpecifications = new itemSpecificationsSet();
            if (!string.IsNullOrEmpty(html))
            {
                //Console.WriteLine("Parse Style&Size\n");
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(html);
                try
                {
                    HtmlNodeCollection sku1_nodes = document.DocumentNode.SelectNodes("//ul[@id='j-sku-list-1']/li/a");
                    itemSpecifications.Color = new List<string>();
                    if (sku1_nodes != null)
                    {
                        foreach (HtmlNode node in sku1_nodes)
                        {
                            itemSpecifications.Color.Add(node.Attributes["title"].Value);
                        }
                    }
                    else
                    {
                        itemSpecifications.Color.Add("No Color Available");
                    }

                    HtmlNodeCollection metal_nodes = document.DocumentNode.SelectNodes("//ul[@id='j-sku-list-3']/li/a/img");
                    itemVaraintsSet VariantsSet = new itemVaraintsSet();

                    HtmlNodeCollection size_nodes = document.DocumentNode.SelectNodes("//ul[@id='j-sku-list-2']/li/a");
                    //check sku3 exists
                    if (size_nodes != null)
                    {
                        foreach (HtmlNode node in size_nodes)
                        {
                            itemSpecifications.Size.Add(node.InnerText);
                        }
                    }
                    else
                    {
                        itemSpecifications.Size.Add("No Size Available");
                    }
                    if (metal_nodes != null)
                    {
                        foreach (HtmlNode node in metal_nodes)
                        {
                            if (node.Attributes["bigpic"].Value != null)
                            {
                                itemSpecifications.MetalColor.Add(new itemVaraintsSet() { bigpic = node.Attributes["bigpic"].Value, metalType = node.Attributes["title"].Value });
                            }
                            else
                            {
                                itemSpecifications.MetalColor.Add(new itemVaraintsSet() { bigpic = "No Image", metalType = node.InnerText });
                            }
                        }
                    }
                    else
                    {
                        itemSpecifications.MetalColor.Add(new itemVaraintsSet() { bigpic = "No Image", metalType = "No Data" });
                    }
                }

                catch (Exception ex)
                {
                    Console.WriteLine("Parse Style&Size Error : " + ex.ToString());
                }
            }
            return itemSpecifications;
        }

        public static List<String> getSmallImage(String html, itemDataSet _item)
        {
            List<String> result = new List<String>();

            if (!string.IsNullOrEmpty(html))
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(html);

                //color
                try
                {//*[@id="img"]
                    HtmlNodeCollection image_nodes = document.DocumentNode.SelectNodes("//img");
                    if (image_nodes != null)
                    {
                        if (String.IsNullOrEmpty(_item.ImageLink) || (_item.ImageLink == "No Image"))
                        {
                            string words = image_nodes[0].Attributes["src"].Value;
                            int index = words.LastIndexOf("_");
                            if (index > 0)
                            {
                                words = words.Substring(0, index);
                            }
                            _item.ImageLink = words;
                        }
                        foreach (HtmlNode node in image_nodes)
                        {
                            //Trim Last _ in _100x100 or _50x50
                            string words = node.Attributes["src"].Value;
                            int index = words.LastIndexOf("_");
                            if (index > 0)
                            {
                                words = words.Substring(0, index);
                            }
                            _item.smallImage.Add(words);
                            result.Add(words);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Small Image Error : " + ex.ToString());
                }
            }
            return result;
        }

        public static void getPrice(String html, itemDataSet _item)
        {
            if (!string.IsNullOrEmpty(html))
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(html);
                //Console.WriteLine("Parse Price\n");
                //color
                try
                {//*[@id="img"]
                    _item.OriginalPrice = document.DocumentNode.SelectSingleNode("//*[@id='j-sku-price']").InnerText;
                    HtmlNode SalesPrice = document.DocumentNode.SelectSingleNode("//*[@id='j-sku-discount-price']");
                    if (SalesPrice != null)
                    {
                        _item.SalesPrice = SalesPrice.InnerText;
                    }
                    else
                    {
                        _item.SalesPrice = _item.OriginalPrice;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Parse Price Error : " + ex.ToString());
                }
            }
        }

        #region misc functions
        /// <summary>
        /// Remove HTML tags from string using char array.
        /// </summary>

        public static bool isAttributeMatch(HtmlNode node, String key, String value)
        {
            HtmlAttribute atr = node.Attributes[key];

            if (atr != null)
                if (atr.Value.IndexOf(value) > -1)
                    return true;
                else
                    return false;
            else
                return false;
        }

        public string getMaxItemNumber()
        {
            try
            {
                HtmlDocument document = new HtmlDocument();

                document.LoadHtml(shtml);
                HtmlNode MaxItemNumber = document.DocumentNode.SelectSingleNode("//div[@id='result-info']//strong");
                string maxItem = MaxItemNumber.InnerText;
                return maxItem;
            }
            catch
            {
                Console.WriteLine("Get max item failed");
                return null;
            }
        }

        public string getMaxPage()
        {
            try
            {
                HtmlDocument document = new HtmlDocument();

                document.LoadHtml(shtml);

                HtmlNode MaxPageNumber = document.DocumentNode.SelectSingleNode("//div[@class='ui-pager']//label[@class='ui-pager-label']");
                string maxPage = MaxPageNumber.InnerText;
                int length = maxPage.Length;
                int endLength = length - 10;
                maxPage = maxPage.Substring(5, endLength);
                return maxPage;
            }
            catch
            {
                Console.WriteLine("Get max page failed");
                return null;
            }
        }
        #endregion

        private static string getStringFromUrl(String URL)
        {
            string htmlCode = "";
            for (int attempts = 0; attempts < 5; attempts++)
            // if you really want to keep going until it works, use   for(;;)
            {
                try
                {
                    driver.Navigate().GoToUrl(URL);
                    Thread.Sleep(5000);
                    htmlCode = driver.PageSource;
                    break;
                }
                catch (Exception e)
                {
                    //reset chrome driver
                    driver.Dispose();
                    Process[] prs = Process.GetProcesses();
                    foreach (Process pr in prs)
                    {
                        if (pr.ProcessName == "chrome")
                        {
                            pr.Kill();
                        }
                    }
                    //End my torment
                    driver = new ChromeDriver();
                    driver.Manage().Timeouts().SetPageLoadTimeout(TimeSpan.FromSeconds(-1));
                    driver.Manage().Timeouts().ImplicitlyWait(TimeSpan.FromSeconds(10));
                    Console.WriteLine("Get html exception with " + e.ToString());
                    Console.WriteLine("\n Reset chromeDriver");
                    //save log
                    datasourceContext dx = new datasourceContext();
                    ProductLog logError = new ProductLog();
                    logError.ProductLink = URL;
                    logError.ErrorMessage = e.ToString();
                    logError.CreatedOn = DateTime.Now;
                    logError.LinkID = 1;
                    logError.FetchCount = 1;
                    logError.Status = 5;
                    dx.ProductLog.Add(logError);
                    dx.SaveChanges();
                }
                Thread.Sleep(50); // Possibly a good idea to pause here, explanation below
            }
            return htmlCode;
        }
    }

}