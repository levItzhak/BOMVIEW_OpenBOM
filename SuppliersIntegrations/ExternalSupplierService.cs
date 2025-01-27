using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BOMVIEW.Interfaces;
using BOMVIEW.Models;

namespace BOMVIEW.Services
{
    public class ExternalSupplierService
    {
        private readonly ObservableCollection<ExternalSupplierEntry> _externalSupplierEntries;
        private readonly ILogger _logger;

        public ObservableCollection<ExternalSupplierEntry> ExternalSupplierEntries => _externalSupplierEntries;

        public ExternalSupplierService(ILogger logger)
        {
            _externalSupplierEntries = new ObservableCollection<ExternalSupplierEntry>();
            _logger = logger;
        }

        // Add a new external supplier entry
        public void AddExternalSupplierEntry(ExternalSupplierEntry entry)
        {
            try
            {
                _externalSupplierEntries.Add(entry);
                _logger.LogInfo($"Added external supplier entry for part: {entry.OrderingCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding external supplier entry: {ex.Message}");
                throw;
            }
        }

        // Update an existing external supplier entry
        public void UpdateExternalSupplierEntry(ExternalSupplierEntry updatedEntry)
        {
            try
            {
                var existingEntry = _externalSupplierEntries.FirstOrDefault(e => e.OriginalBomEntryNum == updatedEntry.OriginalBomEntryNum);
                if (existingEntry != null)
                {
                    int index = _externalSupplierEntries.IndexOf(existingEntry);
                    _externalSupplierEntries[index] = updatedEntry;
                    _logger.LogInfo($"Updated external supplier entry for part: {updatedEntry.OrderingCode}");
                }
                else
                {
                    _logger.LogWarning($"External supplier entry not found for updating: {updatedEntry.OrderingCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating external supplier entry: {ex.Message}");
                throw;
            }
        }

        // Remove an external supplier entry
        public void RemoveExternalSupplierEntry(int originalBomEntryNum)
        {
            try
            {
                var entryToRemove = _externalSupplierEntries.FirstOrDefault(e => e.OriginalBomEntryNum == originalBomEntryNum);
                if (entryToRemove != null)
                {
                    _externalSupplierEntries.Remove(entryToRemove);
                    _logger.LogInfo($"Removed external supplier entry for part: {entryToRemove.OrderingCode}");
                }
                else
                {
                    _logger.LogWarning($"External supplier entry not found for removal: BomEntry #{originalBomEntryNum}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error removing external supplier entry: {ex.Message}");
                throw;
            }
        }

        // Check if a BomEntry has an external supplier entry
        public bool HasExternalSupplier(int bomEntryNum)
        {
            return _externalSupplierEntries.Any(e => e.OriginalBomEntryNum == bomEntryNum);
        }

        // Get an external supplier entry by BomEntry number
        public ExternalSupplierEntry GetByBomEntryNum(int bomEntryNum)
        {
            return _externalSupplierEntries.FirstOrDefault(e => e.OriginalBomEntryNum == bomEntryNum);
        }
    }
}