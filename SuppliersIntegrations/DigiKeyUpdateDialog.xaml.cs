using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BOMVIEW.Models;
using BOMVIEW.Interfaces;
using BOMVIEW.OpenBOM.Models;
using System.Net.Http.Headers;
using System.Net.Http;
using BOMVIEW.Controls;

namespace BOMVIEW
{
    public partial class DigiKeyUpdateDialog : Window
    {
        private readonly BomTreeNode _selectedNode;
        private readonly string _catalogId;
        private readonly string _partNumber;
        private readonly ILogger _logger;
        private readonly DigiKeyService _digiKeyService;
        private readonly OpenBomService _openBomService;
        private DigiKeyProductResponse _digiKeyData;
        private readonly List<CheckBox> _allCheckboxes;
        public DigiKeyUpdateDialog(BomTreeNode node, ILogger logger, DigiKeyService digiKeyService, OpenBomService openBomService)
        {
            InitializeComponent();
            _selectedNode = node;
            _catalogId = node.Id;
            _partNumber = node.PartNumber;
            _logger = logger;
            _digiKeyService = digiKeyService;
            _openBomService = openBomService;

            if (string.IsNullOrEmpty(_catalogId))
            {
                _logger.LogError("No catalog ID found for the selected item.");
                MessageBox.Show("No catalog ID found for the selected item.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // Initialize checkboxes list
            _allCheckboxes = new List<CheckBox>
        {
            ThumbnailCheckbox,
            DescriptionCheckbox,
            CostCheckbox,
            LeadTimeCheckbox,
            ManufacturerCheckbox,
            ManufacturerPartNumberCheckbox,
            VendorCheckbox,
            ProductUrlCheckbox,
            DatasheetCheckbox,
            QuantityAvailableCheckbox,
            CatalogInfoCheckbox,
            MoqCheckbox
        };

            PartNumberText.Text = $"Part Number: {_partNumber}";
            LoadData();
        }     

        private async void LoadData()
        {
            try
            {
                UpdateButton.IsEnabled = false;
                _logger.LogInfo($"Fetching DigiKey data for part number: {_selectedNode.PartNumber}");

                var supplierData = await _digiKeyService.GetPriceAndAvailabilityAsync(_selectedNode.PartNumber);

                // Log the raw supplier data
                _logger.LogInfo($"Raw supplier data: {System.Text.Json.JsonSerializer.Serialize(supplierData)}");

                if (supplierData == null)
                {
                    _logger.LogError("Received null supplier data from DigiKey");
                    MessageBox.Show("Failed to retrieve data from DigiKey.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                if (!supplierData.IsAvailable)
                {
                    _logger.LogWarning($"Part {_selectedNode.PartNumber} not available in DigiKey");
                    MessageBox.Show("Part not found in DigiKey database.", "Part Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                    return;
                }

                // Convert SupplierData to DigiKeyProductResponse
                _digiKeyData = new DigiKeyProductResponse
                {
                    Product = new DigiKeyProduct
                    {
                        IsAvailable = supplierData.IsAvailable,
                        ProductUrl = supplierData.ProductUrl,
                        UnitPrice = supplierData.Price,
                        QuantityAvailable = supplierData.Availability,
                        Description = new DigiKeyDescription
                        {
                            ProductDescription = supplierData.Description ?? "No description available"
                        },
                        Manufacturer = new DigiKeyManufacturer
                        {
                            Name = supplierData.Manufacturer ?? "Unknown Manufacturer"
                        },
                        Parameters = supplierData.Parameters?.Select(p => new DigiKeyParameter
                        {
                            ParameterText = p.Name,
                            ValueText = p.Value
                        }).ToArray() ?? Array.Empty<DigiKeyParameter>(),
                        ProductVariations = new List<DigiKeyProductVariation>
                {
                    new DigiKeyProductVariation
                    {
                        MinimumOrderQuantity = supplierData.PriceBreaks.Any()
                            ? supplierData.PriceBreaks.Min(pb => pb.Quantity)
                            : 1,
                        StandardPricing = supplierData.PriceBreaks.Select(pb => new DigiKeyStandardPricing
                        {
                            BreakQuantity = pb.Quantity,
                            UnitPrice = pb.UnitPrice,
                            TotalPrice = pb.UnitPrice * pb.Quantity
                        }).ToList(),
                        QuantityAvailableforPackageType = supplierData.Availability
                    }
                },
                        PhotoUrl = supplierData.ImageUrl,
                        DatasheetUrl = supplierData.DatasheetUrl,
                        ManufacturerLeadWeeks = supplierData.LeadTime?.ToString() ?? "Unknown"
                    }
                };

                // Log the converted data
                _logger.LogInfo("DigiKey data after conversion:");
                _logger.LogInfo($"Description: {_digiKeyData?.Product?.Description?.ProductDescription}");
                _logger.LogInfo($"Manufacturer: {_digiKeyData?.Product?.Manufacturer?.Name}");
                _logger.LogInfo($"Lead Time: {_digiKeyData?.Product?.ManufacturerLeadWeeks}");
                _logger.LogInfo($"Unit Price: {_digiKeyData?.Product?.UnitPrice}");
                _logger.LogInfo($"Quantity Available: {_digiKeyData?.Product?.QuantityAvailable}");
                _logger.LogInfo($"Product URL: {_digiKeyData?.Product?.ProductUrl}");
                _logger.LogInfo($"Datasheet URL: {_digiKeyData?.Product?.DatasheetUrl}");
                _logger.LogInfo($"Photo URL: {_digiKeyData?.Product?.PhotoUrl}");

                UpdateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading DigiKey data: {ex.Message}");
                MessageBox.Show($"Error loading DigiKey data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void SelectAll_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isChecked = SelectAllCheckbox.IsChecked ?? false;
                _logger.LogInfo($"Select All checkbox {(isChecked ? "checked" : "unchecked")}");

                foreach (var checkbox in _allCheckboxes)
                {
                    checkbox.IsChecked = isChecked;
                    _logger.LogInfo($"Setting {checkbox.Name} to {isChecked}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in SelectAll_CheckedChanged: {ex.Message}");
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateButton.IsEnabled = false;
                LoadingOverlayControl.Show("Updating part information...");

                // If image update is selected, do it first and COMPLETELY SEPARATE from property updates
                if (ThumbnailCheckbox.IsChecked ?? false)
                {
                    try
                    {
                        LoadingOverlayControl.LoadingMessage = "Updating thumbnail...";
                        await UpdateThumbnail();
                        _logger.LogSuccess("Thumbnail update completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating thumbnail: {ex.Message}");
                        MessageBox.Show($"Error updating thumbnail: {ex.Message}", "Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // Only proceed with property updates if there are properties to update
                var propertiesToUpdate = GetPropertiesToUpdate();
                if (propertiesToUpdate.Any())
                {
                    LoadingOverlayControl.LoadingMessage = "Updating properties...";
                    await UpdateCatalogItem(propertiesToUpdate);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in update process: {ex.Message}");
                MessageBox.Show($"Error updating part: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateButton.IsEnabled = true;
                LoadingOverlayControl.Hide();
            }
        }


        private Dictionary<string, string> GetPropertiesToUpdate()
        {
            var properties = new Dictionary<string, string>();

            // Only add properties that are checked and have values
            if (DescriptionCheckbox.IsChecked ?? false)
            {
                var description = _digiKeyData?.Product?.Description?.ProductDescription?.Trim();
                if (!string.IsNullOrEmpty(description))
                    properties["Description"] = description;
            }

            if (CostCheckbox.IsChecked ?? false)
            {
                properties["Cost"] = _digiKeyData?.Product?.UnitPrice.ToString("F2") ?? "0.00";
            }

            if (LeadTimeCheckbox.IsChecked ?? false)
            {
                var leadTime = _digiKeyData?.Product?.ManufacturerLeadWeeks?.Trim();
                if (!string.IsNullOrEmpty(leadTime))
                    properties["Lead time"] = leadTime;
            }

            if (ManufacturerCheckbox.IsChecked ?? false)
            {
                var manufacturer = _digiKeyData?.Product?.Manufacturer?.Name?.Trim();
                if (!string.IsNullOrEmpty(manufacturer))
                    properties["Manufacturer"] = manufacturer;
            }

            if (ManufacturerPartNumberCheckbox.IsChecked ?? false)
            {
                var mpn = _digiKeyData?.Product?.Parameters
                    ?.FirstOrDefault(p => p.ParameterText?.Trim().Equals("Manufacturer Part Number", StringComparison.OrdinalIgnoreCase) == true)
                    ?.ValueText?.Trim() ?? _partNumber;
                if (!string.IsNullOrEmpty(mpn))
                    properties["Manufacturer Part Number"] = mpn;
            }

            if (VendorCheckbox.IsChecked ?? false)
            {
                properties["Vendor"] = "DIGI-KEY CORPORATION";
            }

            if (ProductUrlCheckbox.IsChecked ?? false)
            {
                var productUrl = _digiKeyData?.Product?.ProductUrl?.Trim();
                if (!string.IsNullOrEmpty(productUrl))
                    properties["Link"] = productUrl;
            }

            if (DatasheetCheckbox.IsChecked ?? false)
            {
                var datasheet = _digiKeyData?.Product?.DatasheetUrl?.Trim();
                if (!string.IsNullOrEmpty(datasheet))
                    properties["Data Sheet"] = datasheet;
            }

            if (QuantityAvailableCheckbox.IsChecked ?? false)
            {
                properties["Quantity Available"] = _digiKeyData?.Product?.QuantityAvailable.ToString() ?? "0";
            }

            if (CatalogInfoCheckbox.IsChecked ?? false)
            {
                var categoryName = _digiKeyData?.Product?.Category?.Name?.Trim();
                if (!string.IsNullOrEmpty(categoryName))
                {
                    properties["Catalog Type"] = _digiKeyData.Product.Category.Name;
                }
            }

            if (MoqCheckbox.IsChecked ?? false)
            {
                var moq = _digiKeyData?.Product?.ProductVariations?.FirstOrDefault()?.MinimumOrderQuantity ?? 1;
                properties["Minimum Order Quantity"] = moq.ToString();
            }

            return properties;
        }



        private async Task UpdateCatalogItem(Dictionary<string, string> properties)
        {
            try
            {
                var request = new OpenBomPartRequest
                {
                    PartNumber = _partNumber,  // Use the stored part number
                    Properties = properties
                };

                _logger.LogInfo($"Updating catalog item {_partNumber} in catalog {_catalogId}");
                await _openBomService.UpdateCatalogPartAsync(_catalogId, request);
                _logger.LogSuccess($"Successfully updated part {_partNumber} in OpenBOM");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating catalog item: {ex.Message}");
                throw;
            }
        }


        private async Task UpdateThumbnail()
        {
            try
            {
                if (string.IsNullOrEmpty(_digiKeyData?.Product?.PhotoUrl))
                {
                    _logger.LogWarning("No photo URL available for thumbnail update");
                    return;
                }

                _logger.LogInfo($"Starting image download from: {_digiKeyData.Product.PhotoUrl}");
                _logger.LogInfo($"Updating thumbnail for part number: {_partNumber}");

                var imageBytes = await _digiKeyService.GetImageBytesAsync(_digiKeyData.Product.PhotoUrl);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("No image data received");
                    return;
                }

                _logger.LogInfo($"Successfully downloaded image ({imageBytes.Length} bytes). Uploading to OpenBOM...");
                _logger.LogInfo($"Catalog ID: {_catalogId}, Part Number: {_partNumber}");

                // Use the OpenBomService to upload the image with the correct part number
                await _openBomService.UploadCatalogImageAsync(
                    _catalogId,
                    _partNumber,  // Use the stored part number
                    imageBytes,
                    "Thumbnail image"
                );

                _logger.LogSuccess($"Successfully uploaded thumbnail to OpenBOM for part {_partNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in thumbnail update: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }



        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}