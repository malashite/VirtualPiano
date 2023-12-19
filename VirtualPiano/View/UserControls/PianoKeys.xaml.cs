using NAudio.Wave;
using Newtonsoft.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace VirtualPiano.View.UserControls
{
    public partial class PianoKeys : UserControl
    {
        private HelpWindow helpWindow;
        private Dictionary<string, CancellationTokenSource> noteCancellationTokens = new Dictionary<string, CancellationTokenSource>();
        private Dictionary<string, Task> notePlayingTasks = new Dictionary<string, Task>();
        private List<MusicalEvent> musicalEvents = new List<MusicalEvent>();
        private bool isRecording = false;
        private PlayMode currentPlayMode = PlayMode.DurationMode;
        private string lastNote;
        private const string SequenceFolder = "Configuring";
        private const string SequenceFileName = "recorded_sequence.json";
        private string SequenceFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SequenceFolder, SequenceFileName);

        public PianoKeys()
        {
            InitializeComponent();
            DisplayRecordedSequence();
        }

        private enum PlayMode
        {
            DurationMode,
            ClassicMode
        }

        private class MusicalEvent
        {
            public string Key { get; set; }
            public DateTime PressTimestamp { get; set; } = DateTime.Now;
            public int Duration { get; set; }
            public int Delay { get; set; }
        }

        private void DisplayRecordedSequence()
        {
            try
            {
                if (File.Exists(SequenceFilePath))
                {
                    string sequence = File.ReadAllText(SequenceFilePath);

                    if (!string.IsNullOrEmpty(sequence))
                    {
                        var events = JsonConvert.DeserializeObject<List<MusicalEvent>>(sequence);

                        if (events != null)
                        {
                            var lines = new List<string>();

                            for (int i = 0; i < events.Count; i++)
                            {
                                var item = events[i];
                                var delayInfo = (i < events.Count - 1) ? $", Delay: {events[i + 1].Delay}ms" : "";

                                lines.Add($"Key: {item.Key}, Duration: {item.Duration}ms{delayInfo}");
                            }

                            if (tbSequence != null)
                            {
                                tbSequence.Text = string.Join(Environment.NewLine, lines);
                            }
                            else
                            {
                                LogError("TextBox (tbSequence) equals null");
                            }
                        }
                        else
                        {
                            LogError("Deserialization resulted in a null list of MusicalEvent");
                        }
                    }
                    else
                    {
                        LogError("The content of the file is empty");
                    }
                }
                else
                {
                    LogError("File not found: " + SequenceFilePath);
                }
            }
            catch (IOException ex)
            {
                LogError($"Error reading recorded sequence: {ex.Message}");
            }
            catch (JsonException ex)
            {
                LogError($"Error deserializing recorded sequence: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                LogError($"Error in DisplayRecordedSequence: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error reading recorded sequence: {ex.Message}");
            }
        }


        private void MenuItem_DurationMode_Click(object sender, RoutedEventArgs e)
        {
            currentPlayMode = PlayMode.DurationMode;
            CurrentModeText.Text = "Current Mode: Duration";
        }

        private void MenuItem_ClassicMode_Click(object sender, RoutedEventArgs e)
        {
            currentPlayMode = PlayMode.ClassicMode;
            CurrentModeText.Text = "Current Mode: Classic";
        }

        private void btnKey_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button button)
            {
                string note = button.Name.Replace("btnKey_", "");
                PlayNoteStart(note);
                if (isRecording)
                {
                    if (!musicalEvents.Exists(ev => ev.Key == note))
                    {
                        musicalEvents.Add(new MusicalEvent { Key = note, PressTimestamp = DateTime.Now });
                    }
                }
            }
        }

        private void btnKey_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button button)
            {
                string note = button.Name.Replace("btnKey_", "");
                PlayNoteStop(note);

                if (isRecording)
                {
                    if (musicalEvents.Count > 0)
                    {
                        var lastEvent = musicalEvents.FindLast(ev => ev.Key == note);

                        if (lastEvent != null)
                        {
                            lastEvent.Duration = (int)(DateTime.Now - lastEvent.PressTimestamp).TotalMilliseconds;

                            if (musicalEvents.Count > 1)
                            {
                                lastEvent.Delay = (int)(lastEvent.PressTimestamp - musicalEvents[musicalEvents.Count - 2].PressTimestamp).TotalMilliseconds;
                            }
                            else
                            {
                                lastEvent.Delay = 0;
                            }
                        }
                    }
                }
            }
        }


        private void PianoKeys_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            string note = GetNoteFromKey(e.Key, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));

            if (!string.IsNullOrEmpty(note) && note != lastNote)
            {
                if (noteCancellationTokens.ContainsKey(note))
                {
                    noteCancellationTokens[note]?.Cancel();
                    noteCancellationTokens[note]?.Dispose();
                    noteCancellationTokens.Remove(note);
                    notePlayingTasks.Remove(note);
                }

                CancellationTokenSource cts = new CancellationTokenSource();
                noteCancellationTokens[note] = cts;

                Task task = PlayNoteAsync(note, cts.Token);
                notePlayingTasks[note] = task;

                lastNote = note;

                if (isRecording)
                {
                    musicalEvents.Add(new MusicalEvent { Key = note, PressTimestamp = DateTime.Now });
                }
            }
        }

        private void PianoKeys_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            string note = GetNoteFromKey(e.Key, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));

            if (!string.IsNullOrEmpty(note) && noteCancellationTokens.ContainsKey(note))
            {
                noteCancellationTokens[note]?.Cancel();
                noteCancellationTokens[note]?.Dispose();
                noteCancellationTokens.Remove(note);
                notePlayingTasks.Remove(note);

                lastNote = null;

                if (isRecording && musicalEvents.Count > 0)
                {
                    var lastEvent = musicalEvents.FindLast(ev => ev.Key == note);
                    if (lastEvent != null)
                    {
                        lastEvent.Duration = (int)(DateTime.Now - lastEvent.PressTimestamp).TotalMilliseconds;

                        if (musicalEvents.Count > 1)
                        {
                            lastEvent.Delay = (int)(lastEvent.PressTimestamp - musicalEvents[musicalEvents.Count - 2].PressTimestamp).TotalMilliseconds;
                        }
                        else
                        {
                            lastEvent.Delay = 0;
                        }
                    }
                }
            }
        }

        private void PlayNoteStart(string note)
        {
            if (!string.IsNullOrEmpty(note) && note != lastNote)
            {
                if (noteCancellationTokens.ContainsKey(note))
                {
                    noteCancellationTokens[note]?.Cancel();
                    noteCancellationTokens[note]?.Dispose();
                    noteCancellationTokens.Remove(note);
                    notePlayingTasks.Remove(note);
                }

                CancellationTokenSource cts = new CancellationTokenSource();
                noteCancellationTokens[note] = cts;

                Task task = PlayNoteAsync(note, cts.Token);
                notePlayingTasks[note] = task;

                lastNote = note;

                if (isRecording)
                {
                    musicalEvents.Add(new MusicalEvent { Key = note });
                }
            }
        }

        private void PlayNoteStop(string note)
        {
            if (!string.IsNullOrEmpty(note) && noteCancellationTokens.ContainsKey(note))
            {
                noteCancellationTokens[note]?.Cancel();
                noteCancellationTokens[note]?.Dispose();
                noteCancellationTokens.Remove(note);
                notePlayingTasks.Remove(note);

                lastNote = null;

                if (isRecording && musicalEvents.Count > 0)
                {
                    musicalEvents[musicalEvents.Count - 1].Duration = (int)(DateTime.Now - musicalEvents[musicalEvents.Count - 1].PressTimestamp).TotalMilliseconds;
                }
            }
        }

        private string GetNoteFromKey(Key key, bool isShiftPressed)
        {
            switch (key)
            {
                case Key.D1:
                    return isShiftPressed ? "Cs2" : "c2";
                case Key.D2:
                    return isShiftPressed ? "Ds2" : "d2";
                case Key.D3:
                    return "e2";
                case Key.D4:
                    return isShiftPressed ? "Fs2" : "f2";
                case Key.D5:
                    return isShiftPressed ? "Gs2" : "g2";
                case Key.D6:
                    return isShiftPressed ? "As2" : "a2";
                case Key.D7:
                    return "b2";
                case Key.D8:
                    return isShiftPressed ? "Cs3" : "c3";
                case Key.D9:
                    return isShiftPressed ? "Ds3" : "d3";
                case Key.D0:
                    return "e3";
                case Key.Q:
                    return isShiftPressed ? "Fs3" : "f3";
                case Key.W:
                    return isShiftPressed ? "Gs3" : "g3";
                case Key.E:
                    return isShiftPressed ? "As3" : "a3";
                case Key.R:
                    return "b3";
                case Key.T:
                    return isShiftPressed ? "Cs4" : "c4";
                case Key.Y:
                    return isShiftPressed ? "Ds4" : "d4";
                case Key.U:
                    return "e4";
                case Key.I:
                    return isShiftPressed ? "Fs4" : "f4";
                case Key.O:
                    return isShiftPressed ? "Gs4" : "g4";
                case Key.P:
                    return isShiftPressed ? "As4" : "a4";
                case Key.A:
                    return "b4";
                case Key.S:
                    return isShiftPressed ? "Cs5" : "c5";
                case Key.D:
                    return isShiftPressed ? "Ds5" : "d5";
                case Key.F:
                    return "e5";
                case Key.G:
                    return isShiftPressed ? "Fs5" : "f5";
                case Key.H:
                    return isShiftPressed ? "Gs5" : "g5";
                case Key.J:
                    return isShiftPressed ? "As5" : "a5";
                case Key.K:
                    return "b5";
                case Key.L:
                    return isShiftPressed ? "Cs6" : "c6";
                case Key.Z:
                    return isShiftPressed ? "Ds6" : "d6";
                case Key.X:
                    return "e6";
                case Key.C:
                    return isShiftPressed ? "Fs6" : "f6";
                case Key.V:
                    return isShiftPressed ? "Gs6" : "g6";
                case Key.B:
                    return isShiftPressed ? "As6" : "a6";
                case Key.N:
                    return "b6";
                case Key.M:
                    return "c7";
                default:
                    return null;
            }
        }

        private async Task PlayNoteAsync(string note, CancellationToken token)
        {
            string soundsFolder = "Sounds";
            string audioFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, soundsFolder, $"{note}.mp3");

            if (File.Exists(audioFilePath))
            {
                try
                {
                    using (var audioFile = new AudioFileReader(audioFilePath))
                    using (var outputDevice = new WaveOutEvent())
                    {
                        outputDevice.Init(audioFile);
                        outputDevice.Play();

                        if (currentPlayMode == PlayMode.DurationMode)
                        {
                            await Task.Delay(-1, token);
                        }
                        else if (currentPlayMode == PlayMode.ClassicMode)
                        {
                            await Task.Delay(2000);
                        }

                        outputDevice.Stop();
                    }
                    if (currentPlayMode != PlayMode.DurationMode && currentPlayMode != PlayMode.ClassicMode)
                    {
                        throw new InvalidOperationException($"Invalid PlayMode: {currentPlayMode}");
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    LogError($"Error playing note: {ex.Message}");
                }
            }
            else
            {
                LogError($"File not found: {audioFilePath}");
            }
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            string sequenceFolder = "Configuring";
            string sequenceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, sequenceFolder, "recorded_sequence.json");

            LoadFromFile(sequenceFilePath);

            if (musicalEvents.Count > 0)
            {
                PlayRecordedSequenceAsync();
            }
            else
            {
                LogError("No recorded sequence to play.");
            }
        }

        private void btnRecord_Click(object sender, RoutedEventArgs e)
        {
            ToggleRecording(!isRecording);
        }

        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                FlowDocument document = new FlowDocument(new Paragraph(new Run(tbSequence.Text)));
                IDocumentPaginatorSource paginatorSource = document;
                printDialog.PrintDocument(paginatorSource.DocumentPaginator, "Print Results");
            }
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (helpWindow == null || !helpWindow.IsVisible)
                {
                    helpWindow = new HelpWindow();
                    helpWindow.Show();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error opening help window: {ex.Message}");
            }
        }

        private void ToggleRecording(bool startRecording)
        {
            isRecording = startRecording;

            if (isRecording)
            {
                musicalEvents.Clear();
                UpdateTextBox();
                btnRecord.Content = "Stop";
            }
            else
            {
                btnRecord.Content = "Record";
                SaveToFile(SequenceFilePath);
                UpdateTextBox();
            }
        }

        private void SaveToFile(string filePath)
        {
            try
            {
                var json = JsonConvert.SerializeObject(musicalEvents);
                File.WriteAllText(filePath, json);
            }
            catch (IOException ex)
            {
                LogError($"Error writing to file: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error during save: {ex.Message}");
            }
        }

        private void LoadFromFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);

                    if (!string.IsNullOrEmpty(json))
                    {
                        musicalEvents = JsonConvert.DeserializeObject<List<MusicalEvent>>(json);

                        if (musicalEvents == null)
                        {
                            LogError("Deserialization resulted in a null list of MusicalEvent");
                        }
                    }
                    else
                    {
                        LogError("The content of the file is empty");
                    }
                }
                else
                {
                    LogError("File not found: " + filePath);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error loading recorded sequence: {ex.Message}");
            }
        }

        private async Task PlayRecordedSequenceAsync()
        {
            for (int i = 0; i < musicalEvents.Count; i++)
            {
                PlayNoteStart(musicalEvents[i].Key);
                await Task.Delay(musicalEvents[i].Duration);
                PlayNoteStop(musicalEvents[i].Key);
                if (i != musicalEvents.Count - 1)
                {
                    await Task.Delay(musicalEvents[i+1].Delay);
                }
            }
        }

        private void UpdateTextBox()
        {
            tbSequence.Clear();
            for (int i = 0;i < musicalEvents.Count;i++)
            {
                if (i != musicalEvents.Count - 1)
                {
                    tbSequence.AppendText($"Key: {musicalEvents[i].Key}, Duration: {musicalEvents[i].Duration}ms, Delay: {musicalEvents[i + 1].Delay}ms\n");
                }
                else
                {
                    tbSequence.AppendText($"Key: {musicalEvents[i].Key}, Duration: {musicalEvents[i].Duration}ms\n");
                }
            }
        }

        private void LogError(string message)
        {
            MessageBox.Show($"Error: {message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
