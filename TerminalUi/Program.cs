using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geonorge.MassivNedlasting;
using Serilog;

namespace Geonorge.MassivNedlasting.TerminalUi
{
    internal static class Program
    {
        private static AppSettings _appSettings;
        private static ConfigFile _configFile;
        private static DatasetService _datasetService;
        private static List<DownloadViewModel> _selectedDownloads;
        private static string _activeDatasetTitle;

        private static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("log-.txt", rollOnFileSizeLimit: true, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();

            _appSettings = ApplicationService.GetAppSettings();
            _configFile = _appSettings.LastOpendConfigFile ?? ConfigFile.GetDefaultConfigFile();
            _datasetService = new DatasetService(_configFile);
            _datasetService.UpdateProjections();
            _datasetService.ConvertDownloadToDefaultConfigFileIfExists();
            _selectedDownloads = _datasetService.GetSelectedFilesToDownloadAsViewModel();

            PrintHeader();
            Console.WriteLine($"Config: {_configFile.Name}");
            Console.WriteLine($"Settings: {GetSettingsFilePath()}");
            Console.WriteLine($"Download selection file: {_configFile.FilePath}");

            var running = true;
            while (running)
            {
                Console.WriteLine();
                Console.WriteLine("Main menu");
                Console.WriteLine(" Active dataset: " + (_activeDatasetTitle ?? "<none>"));
                Console.WriteLine(" 1) Browse datasets and select files");
                Console.WriteLine(" 2) View/edit selected downloads");
                Console.WriteLine(" 3) Settings/configuration");
                Console.WriteLine(" 4) Save");
                Console.WriteLine(" 5) Save and exit");
                Console.Write("Choose: ");
                var choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                        BrowseDatasets();
                        break;
                    case "2":
                        ShowSelectedDownloads();
                        break;
                    case "3":
                        SettingsMenu();
                        break;
                    case "4":
                        SaveSelection();
                        break;
                    case "5":
                        SaveSelection();
                        running = false;
                        break;
                    default:
                        Console.WriteLine("Unknown choice.");
                        break;
                }
            }
        }

        private static void BrowseDatasets()
        {
            List<Dataset> datasets;
            try
            {
                datasets = _datasetService.GetDatasets();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not load datasets: " + ex.Message);
                return;
            }

            while (true)
            {
                Console.WriteLine();
                Console.Write("Dataset search (blank = all, q = back): ");
                var query = Console.ReadLine();
                if (string.Equals(query, "q", StringComparison.OrdinalIgnoreCase))
                    return;

                var filtered = FilterDatasets(datasets, query).Take(50).ToList();
                if (!filtered.Any())
                {
                    Console.WriteLine("No dataset matches.");
                    continue;
                }

                for (var i = 0; i < filtered.Count; i++)
                {
                    Console.WriteLine($" {i + 1,2}) {filtered[i].Title} [{filtered[i].Organization}]");
                }

                Console.Write("Select dataset number (or Enter to search again): ");
                var selected = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(selected))
                    continue;

                if (TryParseIndex(selected, filtered.Count, out var datasetIndex))
                {
                    EditDatasetSelection(filtered[datasetIndex]);
                }
                else
                {
                    Console.WriteLine("Invalid dataset number.");
                }
            }
        }

        private static void EditDatasetSelection(Dataset dataset)
        {
            _activeDatasetTitle = dataset.Title;
            Console.WriteLine();
            Console.WriteLine("Dataset: " + dataset.Title);

            List<DatasetFileViewModel> files;
            try
            {
                files = _datasetService.GetDatasetFiles(dataset);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not load files for dataset: " + ex.Message);
                _activeDatasetTitle = null;
                return;
            }

            if (!files.Any())
            {
                Console.WriteLine("No files available for this dataset.");
                _activeDatasetTitle = null;
                return;
            }

            MarkAlreadySelected(files);

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Active dataset: " + dataset.Title);
                Console.WriteLine("Dataset actions");
                Console.WriteLine(" 1) Search and add files");
                Console.WriteLine(" 2) Remove files from selection");
                Console.WriteLine(" 3) Toggle subscribe mode");
                Console.WriteLine(" 4) Toggle projection/format filters for subscribe mode");
                Console.WriteLine(" 5) Back");
                Console.Write("Choose: ");
                var action = Console.ReadLine()?.Trim();

                switch (action)
                {
                    case "1":
                        SearchAndSelectFiles(dataset, files, true);
                        break;
                    case "2":
                        SearchAndSelectFiles(dataset, files, false);
                        break;
                    case "3":
                        ToggleSubscribe(dataset);
                        break;
                    case "4":
                        EditSubscribeFilters(dataset);
                        break;
                    case "5":
                        _activeDatasetTitle = null;
                        return;
                    default:
                        Console.WriteLine("Unknown choice.");
                        break;
                }
            }
        }

        private static void SearchAndSelectFiles(Dataset dataset, List<DatasetFileViewModel> files, bool add)
        {
            Console.WriteLine("Active dataset: " + dataset.Title);
            Console.Write(add ? "Search files to add (blank = all): " : "Search files to remove (blank = all): ");
            var query = Console.ReadLine();

            var filtered = FilterFiles(files, query).Take(200).ToList();
            if (!filtered.Any())
            {
                Console.WriteLine("No files match search.");
                return;
            }

            for (var i = 0; i < filtered.Count; i++)
            {
                var marker = filtered[i].SelectedForDownload ? "[x]" : "[ ]";
                Console.WriteLine($" {i + 1,3}) {marker} {filtered[i].Title} | {filtered[i].Category} | {filtered[i].Format}");
            }

            Console.Write(add
                ? "Enter numbers to add (comma-separated, e.g. 1,3,5): "
                : "Enter numbers to remove (comma-separated, e.g. 1,3,5): ");

            var input = Console.ReadLine();
            var indexes = ParseIndexes(input, filtered.Count);
            if (!indexes.Any())
            {
                Console.WriteLine("No valid indexes selected.");
                return;
            }

            foreach (var idx in indexes)
            {
                var file = filtered[idx];
                if (add)
                {
                    AddToSelection(dataset, file);
                    file.SelectedForDownload = true;
                }
                else
                {
                    RemoveFromSelection(file);
                    file.SelectedForDownload = false;
                }
            }

            Console.WriteLine(add ? "Files added." : "Files removed.");
        }

        private static void ToggleSubscribe(Dataset dataset)
        {
            var existing = _selectedDownloads.FirstOrDefault(d => d.DatasetId == dataset.Uuid || d.DatasetTitle == dataset.Title);
            var current = existing != null && existing.Subscribe;
            var next = !current;

            if (existing == null && next)
            {
                _selectedDownloads.Add(new DownloadViewModel(dataset, true));
            }
            else if (existing != null)
            {
                existing.Subscribe = next;
                if (next)
                {
                    existing.AutoAddFiles = true;
                    existing.AutoDeleteFiles = true;
                }
                else
                {
                    existing.AutoAddFiles = false;
                    existing.AutoDeleteFiles = false;
                    if (!existing.Files.Any())
                    {
                        _selectedDownloads.Remove(existing);
                    }
                }
            }

            Console.WriteLine(next
                ? "Subscribe enabled for dataset (auto add/delete enabled)."
                : "Subscribe disabled for dataset.");
        }

        private static void EditSubscribeFilters(Dataset dataset)
        {
            var existing = _selectedDownloads.FirstOrDefault(d => d.DatasetId == dataset.Uuid || d.DatasetTitle == dataset.Title);
            if (existing == null)
            {
                Console.WriteLine("Dataset is not yet in selection. Add a file or enable subscribe first.");
                return;
            }

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"Subscribe settings for {dataset.Title}");
                Console.WriteLine($" AutoAddFiles: {existing.AutoAddFiles}");
                Console.WriteLine($" AutoDeleteFiles: {existing.AutoDeleteFiles}");
                Console.WriteLine(" 1) Toggle AutoAddFiles");
                Console.WriteLine(" 2) Toggle AutoDeleteFiles");
                Console.WriteLine(" 3) Toggle projection filter items");
                Console.WriteLine(" 4) Toggle format filter items");
                Console.WriteLine(" 5) Back");
                Console.Write("Choose: ");
                var choice = Console.ReadLine()?.Trim();

                if (choice == "1") existing.AutoAddFiles = !existing.AutoAddFiles;
                else if (choice == "2") existing.AutoDeleteFiles = !existing.AutoDeleteFiles;
                else if (choice == "3") ToggleProjectionFilters(existing);
                else if (choice == "4") ToggleFormatFilters(existing);
                else if (choice == "5") return;
                else Console.WriteLine("Unknown choice.");
            }
        }

        private static void ToggleProjectionFilters(DownloadViewModel download)
        {
            if (download.Projections == null || !download.Projections.Any())
            {
                Console.WriteLine("No projection filters available.");
                return;
            }

            for (var i = 0; i < download.Projections.Count; i++)
            {
                var p = download.Projections[i];
                Console.WriteLine($" {i + 1,2}) [{(p.Selected ? 'x' : ' ')}] {p.Name}");
            }

            Console.Write("Indexes to toggle: ");
            var indexes = ParseIndexes(Console.ReadLine(), download.Projections.Count);
            foreach (var idx in indexes)
            {
                download.Projections[idx].Selected = !download.Projections[idx].Selected;
            }
        }

        private static void ToggleFormatFilters(DownloadViewModel download)
        {
            if (download.Formats == null || !download.Formats.Any())
            {
                Console.WriteLine("No format filters available.");
                return;
            }

            for (var i = 0; i < download.Formats.Count; i++)
            {
                var f = download.Formats[i];
                Console.WriteLine($" {i + 1,2}) [{(f.Selected ? 'x' : ' ')}] {f.Name}");
            }

            Console.Write("Indexes to toggle: ");
            var indexes = ParseIndexes(Console.ReadLine(), download.Formats.Count);
            foreach (var idx in indexes)
            {
                download.Formats[idx].Selected = !download.Formats[idx].Selected;
            }
        }

        private static void AddToSelection(Dataset dataset, DatasetFileViewModel selectedFile)
        {
            if (selectedFile == null)
                return;

            var datasetSelection = _selectedDownloads.FirstOrDefault(d => d.DatasetId == selectedFile.DatasetId || d.DatasetTitle == selectedFile.DatasetId);
            if (datasetSelection == null)
            {
                _selectedDownloads.Add(new DownloadViewModel(dataset, selectedFile));
                return;
            }

            if (datasetSelection.Files.All(f => f.Id != selectedFile.Id))
            {
                datasetSelection.Files.Add(selectedFile);
            }
        }

        private static void RemoveFromSelection(DatasetFileViewModel selectedFile)
        {
            foreach (var dataset in _selectedDownloads.ToList())
            {
                dataset.Files.RemoveAll(f => f.Id == selectedFile.Id);
                if (!dataset.Files.Any() && !dataset.Subscribe)
                {
                    _selectedDownloads.Remove(dataset);
                }
            }
        }

        private static void ShowSelectedDownloads()
        {
            if (!_selectedDownloads.Any())
            {
                Console.WriteLine("No selected downloads.");
                return;
            }

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Selected downloads:");
                for (var i = 0; i < _selectedDownloads.Count; i++)
                {
                    var d = _selectedDownloads[i];
                    Console.WriteLine($" {i + 1,2}) {d.DatasetTitle} | files: {d.Files.Count} | subscribe: {d.Subscribe}");
                }

                Console.WriteLine(" a) Remove one dataset selection");
                Console.WriteLine(" b) Remove all selections");
                Console.WriteLine(" q) Back");
                Console.Write("Choose: ");
                var choice = Console.ReadLine()?.Trim().ToLowerInvariant();

                if (choice == "q")
                    return;

                if (choice == "a")
                {
                    Console.Write("Dataset number to remove: ");
                    var idxInput = Console.ReadLine();
                    if (TryParseIndex(idxInput, _selectedDownloads.Count, out var index))
                    {
                        _selectedDownloads.RemoveAt(index);
                    }
                    else
                    {
                        Console.WriteLine("Invalid number.");
                    }
                }
                else if (choice == "b")
                {
                    Console.Write("Really remove all selected downloads? (y/n): ");
                    var confirm = Console.ReadLine();
                    if (string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedDownloads.Clear();
                    }
                }
            }
        }

        private static void SettingsMenu()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Settings");
                Console.WriteLine($" Active config: {_configFile.Name}");
                Console.WriteLine($" Download directory: {_configFile.DownloadDirectory}");
                Console.WriteLine($" Log directory: {_configFile.LogDirectory}");
                Console.WriteLine($" Username: {_appSettings.Username}");
                Console.WriteLine(" 1) Change active config");
                Console.WriteLine(" 2) Edit download directory");
                Console.WriteLine(" 3) Edit log directory");
                Console.WriteLine(" 4) Edit credentials");
                Console.WriteLine(" 5) Back");
                Console.Write("Choose: ");
                var choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                        ChangeConfig();
                        break;
                    case "2":
                        EditPath("download", p => _configFile.DownloadDirectory = p);
                        break;
                    case "3":
                        EditPath("log", p => _configFile.LogDirectory = p);
                        break;
                    case "4":
                        EditCredentials();
                        break;
                    case "5":
                        return;
                    default:
                        Console.WriteLine("Unknown choice.");
                        break;
                }
            }
        }

        private static void ChangeConfig()
        {
            if (!_appSettings.ConfigFiles.Any())
            {
                Console.WriteLine("No config files defined.");
                return;
            }

            for (var i = 0; i < _appSettings.ConfigFiles.Count; i++)
            {
                Console.WriteLine($" {i + 1,2}) {_appSettings.ConfigFiles[i].Name}");
            }

            Console.Write("Select config number: ");
            var input = Console.ReadLine();
            if (!TryParseIndex(input, _appSettings.ConfigFiles.Count, out var idx))
            {
                Console.WriteLine("Invalid config number.");
                return;
            }

            SaveSelection();
            _configFile = _appSettings.ConfigFiles[idx];
            _appSettings.LastOpendConfigFile = _configFile;
            ApplicationService.WriteToAppSettingsFile(_appSettings);
            _datasetService = new DatasetService(_configFile);
            _selectedDownloads = _datasetService.GetSelectedFilesToDownloadAsViewModel();
            Console.WriteLine("Switched config to: " + _configFile.Name);
        }

        private static void EditPath(string pathType, Action<string> setter)
        {
            Console.Write($"Enter new {pathType} directory path: ");
            var path = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("Path not changed.");
                return;
            }

            try
            {
                Directory.CreateDirectory(path);
                setter(path);
                UpdateConfigInSettings();
                ApplicationService.WriteToAppSettingsFile(_appSettings);
                Console.WriteLine("Path updated.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not set directory: " + ex.Message);
            }
        }

        private static void EditCredentials()
        {
            Console.Write("Username (blank to keep current): ");
            var username = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(username))
                _appSettings.Username = username.Trim();

            Console.Write("Password (blank to keep current, '-' to clear): ");
            var password = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(password))
            {
                if (password == "-")
                {
                    _appSettings.Password = null;
                }
                else
                {
                    _appSettings.Password = ProtectionService.CreateProtectedPassword(password);
                }
            }

            ApplicationService.WriteToAppSettingsFile(_appSettings);
            Console.WriteLine("Credentials updated in settings.json.");
        }

        private static void UpdateConfigInSettings()
        {
            for (var i = 0; i < _appSettings.ConfigFiles.Count; i++)
            {
                if (_appSettings.ConfigFiles[i].Id == _configFile.Id)
                {
                    _appSettings.ConfigFiles[i] = _configFile;
                    _appSettings.LastOpendConfigFile = _configFile;
                    return;
                }
            }

            _appSettings.ConfigFiles.Add(_configFile);
            _appSettings.LastOpendConfigFile = _configFile;
        }

        private static void SaveSelection()
        {
            try
            {
                _datasetService.WriteToConfigFile(_selectedDownloads);
                UpdateConfigInSettings();
                ApplicationService.WriteToAppSettingsFile(_appSettings);
                Console.WriteLine("Saved download selection and settings.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while saving: " + ex.Message);
            }
        }

        private static void MarkAlreadySelected(List<DatasetFileViewModel> files)
        {
            var selectedIds = new HashSet<string>(_selectedDownloads.SelectMany(d => d.Files).Select(f => f.Id));
            foreach (var file in files)
            {
                file.SelectedForDownload = selectedIds.Contains(file.Id);
            }
        }

        private static IEnumerable<Dataset> FilterDatasets(IEnumerable<Dataset> datasets, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return datasets;

            return datasets.Where(d => Contains(d.Title, query) || Contains(d.Organization, query));
        }

        private static IEnumerable<DatasetFileViewModel> FilterFiles(IEnumerable<DatasetFileViewModel> files, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return files;

            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return files.Where(file => words.All(word =>
                Contains(file.Title, word)
                || Contains(file.Category, word)
                || Contains(file.AreaCode, word)
                || Contains(file.AreaLabel, word)
                || Contains(file.Format, word)
                || Contains(file.County, word)));
        }

        private static bool Contains(string input, string query)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(query))
                return false;

            return input.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<int> ParseIndexes(string input, int maxCount)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(input))
                return result;

            foreach (var chunk in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(chunk.Trim(), out var n) && n > 0 && n <= maxCount)
                {
                    result.Add(n - 1);
                }
            }

            return result.Distinct().ToList();
        }

        private static bool TryParseIndex(string input, int maxCount, out int index)
        {
            index = -1;
            if (!int.TryParse(input, out var selected) || selected < 1 || selected > maxCount)
                return false;

            index = selected - 1;
            return true;
        }

        private static string GetSettingsFilePath()
        {
            return Path.Combine(ApplicationService.GetAppDirectory().FullName, "settings.json");
        }

        private static void PrintHeader()
        {
            Console.WriteLine("Geonorge Nedlasting - Terminal UI");
            Console.WriteLine("---------------------------------");
            Console.WriteLine("Linux-friendly replacement for the Windows GUI.");
        }
    }
}
