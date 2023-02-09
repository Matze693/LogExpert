﻿using System;
using System.Collections.Generic;
using LogExpert;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CsvHelper;
using Newtonsoft.Json;

namespace CsvColumnizer
{
    /// <summary>
    /// This Columnizer can parse CSV files. It uses the IInitColumnizer interface for support of dynamic field count.
    /// The IPreProcessColumnizer is implemented to read field names from the very first line of the file. Then
    /// the line is dropped. So it's not seen by LogExpert. The field names will be used as column names.
    /// </summary>
    public partial class CsvColumnizer : ILogLineColumnizer, IInitColumnizer, IColumnizerConfigurator, IPreProcessColumnizer, IColumnizerPriority
    {
        #region Fields

        private static readonly string _configFileName = "csvcolumnizer.json";

        private readonly IList<CsvColumn> _columnList = new List<CsvColumn>();
        private CsvColumnizerConfig _config;

        private ILogLine _firstLine;

        // if CSV is detected to be 'invalid' the columnizer will behave like a default columnizer
        private bool _isValidCsv;

        #endregion

        #region Public methods

        public string PreProcessLine(string logLine, int lineNum, int realLineNum)
        {
            if (realLineNum == 0)
            {
                // store for later field names and field count retrieval
                _firstLine = new CsvLogLine
                {
                    FullLine = logLine,
                    LineNumber = 0
                };
                if (_config.MinColumns > 0)
                {
                    using (CsvReader csv = new CsvReader(new StringReader(logLine), _config.ReaderConfiguration))
                    {
                        if (csv.ColumnCount < _config.MinColumns)
                        {
                            // on invalid CSV don't hide the first line from LogExpert, since the file will be displayed in plain mode
                            _isValidCsv = false;
                            return logLine;
                        }
                    }
                }

                _isValidCsv = true;
            }

            if (_config.HasFieldNames && realLineNum == 0)
            {
                return null; // hide from LogExpert
            }

            if (_config.CommentChar != ' ' && logLine.StartsWith("" + _config.CommentChar))
            {
                return null;
            }

            return logLine;
        }

        public string GetName()
        {
            return "CSV Columnizer";
        }

        public string GetDescription()
        {
            return "Splits CSV files into columns.\r\n\r\nCredits:\r\nThis Columnizer uses the CsvHelper. https://github.com/JoshClose/CsvHelper. \r\n";
        }

        public int GetColumnCount()
        {
            return _isValidCsv ? _columnList.Count : 1;
        }

        public string[] GetColumnNames()
        {
            string[] names = new string[GetColumnCount()];
            if (_isValidCsv)
            {
                int i = 0;
                foreach (CsvColumn column in _columnList)
                {
                    names[i++] = column.Name;
                }
            }
            else
            {
                names[0] = "Text";
            }

            return names;
        }

        public IColumnizedLogLine SplitLine(ILogLineColumnizerCallback callback, ILogLine line)
        {
            if (_isValidCsv)
            {
                return SplitCsvLine(line);
            }

            ColumnizedLogLine cLogLine = new ColumnizedLogLine();
            cLogLine.LogLine = line;
            cLogLine.ColumnValues = new IColumn[] {new Column {FullValue = line.FullLine, Parent = cLogLine}};

            return cLogLine;
        }

        public bool IsTimeshiftImplemented()
        {
            return false;
        }

        public void SetTimeOffset(int msecOffset)
        {
            throw new NotImplementedException();
        }

        public int GetTimeOffset()
        {
            throw new NotImplementedException();
        }

        public DateTime GetTimestamp(ILogLineColumnizerCallback callback, ILogLine line)
        {
            throw new NotImplementedException();
        }

        public void PushValue(ILogLineColumnizerCallback callback, int column, string value, string oldValue)
        {
            throw new NotImplementedException();
        }

        public void Selected(ILogLineColumnizerCallback callback)
        {
            if (_isValidCsv) // see PreProcessLine()
            {
                _columnList.Clear();
                ILogLine line = _config.HasFieldNames ? _firstLine : callback.GetLogLine(0);

                if (line != null)
                {
                    using (CsvReader csv = new CsvReader(new StringReader(line.FullLine), _config.ReaderConfiguration))
                    {
                        csv.Read();
                        csv.ReadHeader();
                        var records = csv.GetRecord<dynamic>();
                        int fieldCount = csv.ColumnCount;

                        var header = csv.HeaderRecord;

                        //List<Column> columns = new List<Column>();

                        for (int i = 0; i < fieldCount; ++i)
                        {
                            if (_config.HasFieldNames)
                            {
                                _columnList.Add(new CsvColumn(csv[i]));
                            }
                            else
                            {
                                _columnList.Add(new CsvColumn("Column " + i + 1));
                            }
                        }
                    }
                }
            }
        }

        public void DeSelected(ILogLineColumnizerCallback callback)
        {
            // nothing to do 
        }

        public void Configure(ILogLineColumnizerCallback callback, string configDir)
        {
            string configPath = configDir + "\\" + _configFileName;
            FileInfo fileInfo = new FileInfo(configPath);

            CsvColumnizerConfigDlg dlg = new CsvColumnizerConfigDlg(_config);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _config.VersionBuild = Assembly.GetExecutingAssembly().GetName().Version.Build;

                using (StreamWriter sw = new StreamWriter(fileInfo.Create()))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(sw, _config);
                }

                Selected(callback);
            }
        }

        public void LoadConfig(string configDir)
        {
            string configPath = configDir + "\\" + _configFileName;
            FileSystemInfo fileInfo = new FileInfo(configPath);

            if (fileInfo.Exists == false)
            {
                _config = new CsvColumnizerConfig();
                _config.InitDefaults();
            }
            else
            {
                try
                {
                    _config = JsonConvert.DeserializeObject<CsvColumnizerConfig>(File.ReadAllText($"{fileInfo.FullName}"));
                    _config.ConfigureReaderConfiguration();
                }
                catch (Exception e)
                {
                    MessageBox.Show($"Error while deserializing config data: {e.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _config = new CsvColumnizerConfig();
                    _config.InitDefaults();
                }
            }
        }

        public Priority GetPriority(string fileName, IEnumerable<ILogLine> samples)
        {
            Priority result = Priority.NotSupport;
            if (fileName.EndsWith("csv", StringComparison.OrdinalIgnoreCase))
            {
                result = Priority.CanSupport;
            }

            return result;
        }

        #endregion

        #region Private Methods

        private IColumnizedLogLine SplitCsvLine(ILogLine line)
        {
            ColumnizedLogLine cLogLine = new ColumnizedLogLine();
            cLogLine.LogLine = line;
            
            using (CsvReader csv = new CsvReader(new StringReader(line.FullLine), _config.ReaderConfiguration))
            {
                var records = csv.GetRecord<dynamic>();
                int fieldCount = csv.ColumnCount;

                List<Column> columns = new List<Column>();

                for (int i = 0; i < fieldCount; ++i)
                {
                    columns.Add(new Column {FullValue = csv[i], Parent = cLogLine});
                }

                cLogLine.ColumnValues = columns.Select(a => a as IColumn).ToArray();

                return cLogLine;
            }
        }

        #endregion
    }
}