﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using PegasusExportPlugin.Controls;
using PegasusExportPlugin.Launchbox;
using PegasusExportPlugin.Pegasus;
using Unbroken;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using Resources = PegasusExportPlugin.Properties.Resources;

namespace PegasusExportPlugin
{
    public partial class frmPegasusExport : Form, ISystemMenuItemPlugin
    {
        public frmPegasusExport()
        {
            InitializeComponent();
        }

        private IDataManager _dataManager = PluginHelper.DataManager;

        private Dictionary<string, string> _imageTypeDictionary = new Dictionary<string,string>();
        public string Caption => "Pegasus Export";

        public Image IconImage => Resources.favicon96;

        public bool ShowInLaunchBox => true;

        public bool ShowInBigBox => false;

        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected()
        {
            Show();
        }

        private async void BtnExport_Click(object sender, EventArgs e)
        {
       
            if (!chkAssets.Checked && !chkRoms.Checked && !chkMetaData.Checked)
            {
                MessageBox.Show("Please select at least one item to export.");
                return;
            }

            var selectedFolder = fbdExportFolder.SelectedPath;

            if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
            {
                MessageBox.Show("Please select a valid location.");
                return;
            }

            var checkedItems = clbAssetList.CheckedItems;
            if (checkedItems.Count < 1 && chkAssets.Checked)
            {
                MessageBox.Show("You selected to export assets but no assets are selected.");
                return;
            }

            var platformSettings = (BindingList<PlatformSetting>)dgvPlatforms.DataSource;
            var platformsToExport = platformSettings.Where(platform => platform.Selected && ((platform.ExportApplication && chkRoms.Checked) || (platform.ExportAssets && chkAssets.Checked) || (platform.ExportMetadata && chkMetaData.Checked)));
            if(!platformsToExport.Any())
            {
                MessageBox.Show("You didn't select any platforms/data to export.");
                return;
            }

            btnExport.Enabled = false;

            try
            {
                progressBar.Value = 0;
                await Task.Run(() =>
                {
                    var progress = 0;

                    var platformList = new HashSet<string>(platformsToExport.Select(platform => platform.Name));
                    var platformAssetExportList = new HashSet<string> (platformsToExport.Where(platform => platform.ExportAssets).Select(platform => platform.Name));
                    var platformMetadataExportList = new HashSet<string>(platformsToExport.Where(platform => platform.ExportMetadata).Select(platform => platform.Name));
                    var platformApplicationExportList = new HashSet<string>(platformsToExport.Where(platform => platform.ExportApplication).Select(platform => platform.Name));

                    var games = _dataManager.GetAllGames().Where(game => platformList.Contains(game.Platform)).ToArray();
                    var numberOfGames = games.Length;
                    var gamesByPlatform = games.GroupBy(game => game.Platform);

                    Parallel.ForEach(gamesByPlatform, gamePlatform =>
                    {
                        var platform = gamePlatform.First().Platform;
                        var platformFolderName = FileHelper.CoerceValidFileName(platform);
                        var platformPath = Path.Combine(selectedFolder, platformFolderName);
                        Directory.CreateDirectory(platformPath);
                        var metadataBuilder = new StringBuilder();
                        metadataBuilder.AppendLine($"collection: {platform}");
                        var gamesMetadata = new Dictionary<IGame, StringBuilder>();
                        
                        var imageList = new Dictionary<string, Dictionary<IGame, List<ImageDetails>>>();

                        var fileExtensions = new HashSet<string>();
                        foreach (var game in gamePlatform)
                        {
                            if (chkAssets.Checked && platformAssetExportList.Contains(platform))
                            {
                                var mediaFolder = Path.Combine(platformPath, "media",
                                    Path.GetFileNameWithoutExtension(game.ApplicationPath));
                                Directory.CreateDirectory(mediaFolder);

                                var images = game.GetAllImagesWithDetails();

                                foreach (var image in images)
                                {
                                    if (!string.IsNullOrWhiteSpace(image.FilePath) && File.Exists(image.FilePath))
                                    {
                                        if (_imageTypeDictionary.ContainsKey(image.ImageType))
                                        {
                                            var translatedImageType = _imageTypeDictionary[image.ImageType];
                                            if (checkedItems.Contains(translatedImageType))
                                            {
                                                if (!imageList.ContainsKey(translatedImageType))
                                                {
                                                    imageList.Add(translatedImageType,
                                                        new Dictionary<IGame, List<ImageDetails>>());
                                                }

                                                if (!imageList[translatedImageType].ContainsKey(game))
                                                {
                                                    imageList[translatedImageType].Add(game, new List<ImageDetails>());
                                                }

                                                imageList[translatedImageType][game].Add(image);
                                            }
                                        }

                                    }
                                }

                                if(checkedItems.Contains("video"))
                                {
                                    var video = game.GetVideoPath();
                                    if (!string.IsNullOrWhiteSpace(video) && File.Exists(video))
                                    {
                                        File.Copy(video, Path.Combine(mediaFolder, "video" + Path.GetExtension(video)),
                                            true);
                                    }
                                }
                            }

                            if (chkRoms.Checked && platformApplicationExportList.Contains(platform))
                            {
                                if (!string.IsNullOrWhiteSpace(game.ApplicationPath) && File.Exists(game.ApplicationPath))
                                {
                                    File.Copy(game.ApplicationPath,
                                        Path.Combine(platformPath, Path.GetFileName(game.ApplicationPath)), true);
                                    var fileExtension = Path.GetExtension(game.ApplicationPath).Replace(".", "");
                                    if (!fileExtensions.Contains(fileExtension))
                                    {
                                        fileExtensions.Add(fileExtension);
                                    }
                                }
                            }

                            if (chkMetaData.Checked && platformMetadataExportList.Contains(platform))
                            {
                                var gameMetadataBuilder = new StringBuilder();

                                if (!string.IsNullOrWhiteSpace(game.Title))
                                {
                                    gameMetadataBuilder.AppendLine($"game: {game.Title}");

                                    if (!string.IsNullOrWhiteSpace(game.ApplicationPath))
                                    {
                                        var file = Path.GetFileName(game.ApplicationPath);
                                        gameMetadataBuilder.AppendLine($"file: {file}");

                                        var fileExtension = Path.GetExtension(file).Replace(".","");
                                        if (!fileExtensions.Contains(fileExtension))
                                        {
                                            fileExtensions.Add(fileExtension);
                                        }
                                    }

                                    if (!string.IsNullOrWhiteSpace(game.Developer))
                                    {
                                        gameMetadataBuilder.AppendLine($"developer: {game.Developer}");
                                    }

                                    if (!string.IsNullOrWhiteSpace(game.Publisher))
                                    {
                                        gameMetadataBuilder.AppendLine($"publisher: {game.Publisher}");
                                    }

                                    foreach (var genre in game.Genres)
                                    {
                                        if (!string.IsNullOrWhiteSpace(genre))
                                        {
                                            gameMetadataBuilder.AppendLine($"genre: {genre}");
                                        }
                                    }

                                    if (!string.IsNullOrWhiteSpace(game.Notes))
                                    {
                                        gameMetadataBuilder.AppendLine($"description: {game.Notes}");
                                    }

                                    if (game.ReleaseDate != null)
                                    {
                                        gameMetadataBuilder.AppendLine(
                                            $"release: {((DateTime)game.ReleaseDate).ToString("yyyy-MM-dd")}");
                                    }

                                    if (game.CommunityStarRatingTotalVotes > 0)
                                    {
                                        var rating = (int)(game.CommunityStarRating / 5 * 100);
                                        gameMetadataBuilder.AppendLine($"rating: {rating}");
                                    }
                                    gamesMetadata.Add(game, gameMetadataBuilder);
                                }
                            }

                            Interlocked.Increment(ref progress);

                            BeginInvoke(new MethodInvoker(() =>
                            {
                                progressBar.Value = (int)(progress / (double)numberOfGames * 100);
                            }));
                        }

                        double mode = 0;
                        if (imageList.ContainsKey(PegasusImage.BoxFront))
                        {
                            var aspectRatioGroup = imageList[PegasusImage.BoxFront].SelectMany(game => game.Value).Select(image =>
                            {
                                using (var img = Image.FromFile(image.FilePath))
                                {
                                    return (double)img.Width / (double)img.Height;
                                }

                            }).GroupBy(aspectRatio => aspectRatio);
                            int maxCount = aspectRatioGroup.Max(g => g.Count());
                            mode = aspectRatioGroup.First(g => g.Count() == maxCount).Key;
                        }


                        foreach (var imageType in imageList)
                        {
                            var pegasusImageType = imageType.Key;
                            foreach (var game in imageType.Value)
                            {
                                var mediaFolder = Path.Combine(platformPath, "media",
                                    Path.GetFileNameWithoutExtension(game.Key.ApplicationPath));

                                if (pegasusImageType == PegasusImage.BoxFront)
                                {
                                    var bestImage = game.Value.Aggregate((curMin, image) =>
                                    {
                                        if (curMin == null)
                                        {
                                            return image;
                                        }
                                        else
                                        {
                                            using (var imgMin = Image.FromFile(curMin.FilePath))
                                            using (var img = Image.FromFile(image.FilePath))
                                            {
                                                if (Math.Abs(mode - ((double)img.Width / (double)img.Height)) <
                                                    Math.Abs(mode - ((double)imgMin.Width / (double)imgMin.Height)))
                                                {
                                                    return image;
                                                }
                                                else
                                                {
                                                    return curMin;
                                                }
                                            }
                                        }

                                    });


                                    if (radCopyAssets.Checked)
                                    {
                                        File.Copy(bestImage.FilePath,
                                            Path.Combine(mediaFolder, pegasusImageType + Path.GetExtension(bestImage.FilePath)),
                                            true);
                                    }
                                    else
                                    {
                                        gamesMetadata[game.Key].AppendLine($@"assets.{pegasusImageType}: {bestImage.FilePath}");
                                    }
                                    
                                }
                                else
                                {
                                    var firstImage = game.Value.First();
                                    if (radCopyAssets.Checked)
                                    {
                                        File.Copy(firstImage.FilePath,
                                            Path.Combine(mediaFolder, pegasusImageType + Path.GetExtension(firstImage.FilePath)),
                                            true);
                                    }
                                    else
                                    {
                                        gamesMetadata[game.Key].AppendLine($@"assets.{pegasusImageType}: {firstImage.FilePath}");
                                    }
                                }
                            }
                        }

                        if (chkMetaData.Checked && platformMetadataExportList.Contains(platform))
                        {
                            if (fileExtensions.Count > 0)
                            {
                                metadataBuilder.AppendLine(string.Format(@"extensions: {0}",
                                    string.Join(", ", fileExtensions)));
                            }

                            metadataBuilder.AppendLine("");
                            metadataBuilder.AppendLine(string.Join(Environment.NewLine, gamesMetadata.Select(item => item.Value.ToString())));
                            File.WriteAllText(Path.Combine(platformPath, "metadata.pegasus.txt"), metadataBuilder.ToString());
                        }
                    });
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message);
                throw;
            }
            finally
            {
                btnExport.Enabled = true;
                progressBar.Value = 100;
            }
            
            MessageBox.Show("Done!");
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            if (fbdExportFolder.ShowDialog() == DialogResult.OK)
            {
                txtExportPath.Text = fbdExportFolder.SelectedPath;
            }
        }

        private void BtnUp_Click(object sender, EventArgs e)
        {
            var item = lbImagePriority.SelectedItem;
            if (item != null)
            {
                var newItemIndex = Math.Max(0, lbImagePriority.Items.IndexOf(item) - 1);
                lbImagePriority.Items.Remove(item);
                lbImagePriority.Items.Insert(newItemIndex, item);
                lbImagePriority.SelectedIndex = newItemIndex;
            }
        }

        private void BtnDown_Click(object sender, EventArgs e)
        {
            var item = lbImagePriority.SelectedItem;
            if (item != null)
            {
                var newItemIndex = Math.Min(lbImagePriority.Items.Count - 1, lbImagePriority.Items.IndexOf(item) + 1);
                lbImagePriority.Items.Remove(item);
                lbImagePriority.Items.Insert(newItemIndex, item);
                lbImagePriority.SelectedIndex = newItemIndex;
            }
        }

        private void FrmPegasusExport_Load(object sender, EventArgs e)
        {
            var translationTable = XElement.Load(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"translationTable.xml"));

            var imageTypes = translationTable.Descendants("asset").Select(item =>  item);
            foreach (var imageType in imageTypes)
            {
                if (imageType.Element("key") != null && imageType.Element("value") != null &&
                            !string.IsNullOrWhiteSpace(imageType.Element("key").Value) &&
                            !string.IsNullOrWhiteSpace(imageType.Element("value").Value))
                {
                    _imageTypeDictionary.Add(imageType.Element("key").Value, imageType.Element("value").Value);
                    
                }
            }

            clbAssetList.DataSource = _imageTypeDictionary.Select(image => image.Value).Distinct().ToArray();
            for (int i = 0; i < clbAssetList.Items.Count; i++)
            {
                clbAssetList.SetItemChecked(i,true);
            }

            var platformList = new BindingList<Launchbox.PlatformSetting>(_dataManager.GetAllPlatforms().Select(platform => new Launchbox.PlatformSetting() { Name = platform.Name }).ToList());
            dgvPlatforms.AutoGenerateColumns = false;
            dgvPlatforms.DataSource = platformList;

            
            ((DataGridViewCheckBoxColumnHeaderCell)colSelected.HeaderCell).Select(true);

        }
    }
}