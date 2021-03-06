Imports System.ComponentModel
Imports System.Drawing.Design
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.Management
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms.VisualStyles
Imports Microsoft.Win32
Imports StaxRip.UI
Imports VB6 = Microsoft.VisualBasic

Public Module ShortcutModule
    Public g As New GlobalClass
    Public p As New Project
    Public s As New ApplicationSettings
End Module

Public Enum MediaInformation
    VideoFormat
    DAR
End Enum

Public Class GlobalClass
    Property ProjectPath As String
    Property MainForm As MainForm
    Property MinimizedWindows As Boolean
    Property SavedProject As New Project
    Property DefaultCommands As New GlobalCommands
    Property IsProcessing As Boolean
    Property PreventSaveSettings As Boolean

    ReadOnly Property StartupTemplatePath() As String
        Get
            Dim ret = Folder.Template + s.StartupTemplate + ".srip"

            If Not File.Exists(ret) Then
                ret = Folder.Template + "x264.srip"
                s.StartupTemplate = "x264"
            End If

            Return ret
        End Get
    End Property

    ReadOnly Property SettingsFile() As String
        Get
            Return Folder.Settings + "SettingsV2.dat"
        End Get
    End Property

    Function BrowseFolder(defaultFolder As String) As String
        Using d As New FolderBrowserDialog
            d.Description = "Please select a directory."
            d.SetSelectedPath(defaultFolder)
            If d.ShowDialog = DialogResult.OK Then Return d.SelectedPath
        End Using
    End Function

    Function VerifyRequirements() As Boolean
        For Each pack In Package.Items.Values
            If Not pack.VerifyOK Then Return False
        Next

        If Not p.Script.IsFilterActive("Source") Then
            MsgWarn("No active filter of category 'Source' found.")
            Return False
        End If

        Return True
    End Function

    Function ShowVideoSourceWarnings(files As IEnumerable(Of String)) As Boolean
        For Each i In files
            If Not i.IsANSICompatible AndAlso p.Script.Engine = ScriptEngine.AviSynth Then
                MsgError(Strings.NoUnicode)
                Return True
            End If

            If i.Contains("#") Then
                If Filepath.GetExtFull(i) = ".mp4" OrElse MediaInfo.GetGeneral(i, "Audio_Codec_List").Contains("AAC") Then
                    MsgError("Character # can't be processed by MP4Box, please rename." + BR2 + i)
                    Return True
                End If
            End If

            If i.Length > 170 Then
                MsgError("Generated temp files might exceed 260 character file path limit, please use shorter file paths." + BR2 + i)
                Return True
            End If

            If i.Ext = "dga" Then
                MsgError("There is no properly working x64 source filters available for DGA. There are several newer and faster x64 source filters available.")
                Return True
            End If

            If i.Ext = "dgi" AndAlso File.ReadAllText(i).Contains("DGIndexIM") Then
                MsgError("Please rename the file extension from dgi to dgim.")
                Return True
            End If
        Next
    End Function

    Function Get0ForInfinityOrNaN(arg As Double) As Double
        If Double.IsNaN(arg) OrElse Double.IsInfinity(arg) Then
            Return 0
        Else
            Return arg
        End If
    End Function

    Sub PlayScript(doc As VideoScript)
        If File.Exists(p.Audio0.File) Then
            PlayScript(doc, p.Audio0)
        Else
            PlayScript(doc, p.Audio1)
        End If
    End Sub

    Sub PlayScript(doc As VideoScript, ap As AudioProfile)
        Dim script As New VideoScript
        script.Engine = doc.Engine
        script.Path = p.TempDir + Filepath.GetBase(p.TargetFile) + "_Play." + script.FileType
        script.Filters = doc.GetFiltersCopy

        If script.Engine = ScriptEngine.AviSynth Then
            Dim par = Calc.GetTargetPAR

            If Not par = New Point(1, 1) Then
                Dim targetWidth = CInt((p.TargetHeight * Calc.GetTargetDAR) / 4) * 4
                script.Filters.Add(New VideoFilter("LanczosResize(" & targetWidth & "," & p.TargetHeight & ")"))
            End If

            If File.Exists(ap.File) Then
                script.Filters.Add(New VideoFilter("KillAudio()"))

                Dim nic = Audio.GetNicAudioCode(ap)

                If nic <> "" Then
                    script.Filters.Add(New VideoFilter(nic))
                Else
                    script.Filters.Add(New VideoFilter("AudioDub(last, DirectShowSource(""" + ap.File + """, video = false))"))
                End If

                script.Filters.Add(New VideoFilter("DelayAudio(" & (ap.Delay / 1000).ToString(CultureInfo.InvariantCulture) & ")"))

                Dim cutFilter = script.GetFilter("Cutting")

                If Not cutFilter Is Nothing Then
                    script.RemoveFilter("Cutting")
                    script.Filters.Add(cutFilter)
                End If
            End If
        Else
            Dim par = Calc.GetTargetPAR

            If Not par = New Point(1, 1) Then
                Dim targetWidth = CInt((p.TargetHeight * Calc.GetTargetDAR) / 4) * 4
                script.Filters.Add(New VideoFilter("clip = core.resize.Bicubic(clip, " & targetWidth & "," & p.TargetHeight & ")"))
            End If
        End If

        script.Synchronize(True)
        g.Play(script.Path)
    End Sub

    Function ExtractDelay(value As String) As Integer
        Dim match = Regex.Match(value, " (-?\d+)ms")
        If match.Success Then Return CInt(match.Groups(1).Value)
    End Function

    Sub ShowCode(title As String, content As String)
        Dim f As New HelpForm()
        f.Doc.WriteStart(title)
        f.Doc.Writer.WriteRaw("<pre><code>" + content + "</pre></code>")
        f.Show()
    End Sub

    Sub ShowHelp(title As String, content As String)
        If title <> "" Then title = title.TrimEnd("."c, ":"c)
        MsgInfo(title, content)
    End Sub

    Sub PopulateProfileMenu(
            ic As ToolStripItemCollection,
            profiles As IList,
            dialogAction As Action,
            loadAction As Action(Of Profile))

        For Each iProfile As Profile In profiles
            Dim a = iProfile.Name.SplitNoEmpty("|")
            Dim l = ic

            For i = 0 To a.Length - 1
                Dim found = False

                For Each iItem As ToolStripItem In l
                    If i < a.Length - 1 Then
                        If iItem.Text = a(i) Then
                            found = True
                            l = DirectCast(iItem, ToolStripMenuItem).DropDownItems
                        End If
                    End If
                Next

                If Not found Then
                    If i = a.Length - 1 Then
                        Dim item As New ActionMenuItem(a(i), Sub() loadAction(iProfile))
                        l.Add(item)
                        l = item.DropDownItems
                    Else
                        Dim item As New MenuItemEx(a(i))
                        l.Add(item)
                        l = item.DropDownItems
                    End If
                End If
            Next
        Next

        If Not dialogAction Is Nothing Then
            ic.Add(New ToolStripSeparator)
            ic.Add(New ActionMenuItem("Edit Profiles...", dialogAction, "Opens the profiles editor"))
        End If

        If profiles.Count > 0 Then
            If TypeOf profiles(0) Is Muxer Then

                Dim helpURL = If(g.IsCulture("de"),
                    "http://encodingwissen.de/formate/container.html",
                    "http://forum.doom9.org/showthread.php?t=54306")

                ic.Add(New ActionMenuItem("Help...", Sub() g.ShellExecute(helpURL)))
            ElseIf TypeOf profiles(0) Is AudioProfile Then

                Dim helpURL = If(g.IsCulture("de"),
                    "http://encodingwissen.de/formate/audio.html",
                    "http://en.wikipedia.org/wiki/Advanced_Audio_Coding")

                ic.Add(New ActionMenuItem("Help...", Sub() g.ShellExecute(helpURL)))
            ElseIf TypeOf profiles(0) Is VideoEncoder Then

                Dim helpURL = If(g.IsCulture("de"),
                    "http://encodingwissen.de/codecs",
                    "http://en.wikipedia.org/wiki/Video_codec")

                ic.Add(New ActionMenuItem("Help...", Sub() g.ShellExecute(helpURL)))
            End If
        End If
    End Sub

    Function GetAutoSize(percentage As Integer) As Integer
        Dim ret As Integer
        Dim size = p.TargetSize
        Dim bitrate = p.VideoBitrate

        For i = 1 To 100000
            p.TargetSize = i
            p.VideoBitrate = CInt(Calc.GetVideoBitrate)

            If CInt(Calc.GetPercent) >= percentage Then
                ret = i
                Exit For
            End If
        Next

        p.TargetSize = size
        p.VideoBitrate = bitrate

        If ret = 0 Then ret = size

        Return ret
    End Function

    Function GetPreviewPosMS() As Integer
        Dim fr = p.Script.GetFramerate
        If fr = 0 Then fr = 25
        Return CInt((s.LastPosition / fr) * 1000)
    End Function

    Function GetTextEditor() As String
        Dim ret = GetAssociatedApplication(".txt")
        If ret <> "" Then Return ret
        Return "notepad.exe"
    End Function

    Function GetAssociatedApplication(ext As String) As String
        Dim c = 0UI

        If 1 = Native.AssocQueryString(Native.ASSOCF_VERIFY,
            Native.ASSOCSTR_EXECUTABLE, ext, Nothing, Nothing, c) Then

            If c > 0 Then
                Dim sb As New StringBuilder(CInt(c))

                If 0 = Native.AssocQueryString(Native.ASSOCF_VERIFY,
                    Native.ASSOCSTR_EXECUTABLE, ext, Nothing, sb, c) Then

                    Dim ret = sb.ToString
                    If File.Exists(ret) Then Return ret
                End If
            End If
        End If
    End Function

    Sub SaveSettings()
        Try
            If Not g.PreventSaveSettings Then SafeSerialization.Serialize(s, g.SettingsFile)
        Catch ex As Exception
            g.ShowException(ex)
        End Try
    End Sub

    Sub LoadVideoEncoder(profile As Profile)
        Dim currentMuxer = p.VideoEncoder.Muxer
        p.VideoEncoder = DirectCast(ObjectHelp.GetCopy(profile), VideoEncoder)

        If currentMuxer.IsSupported(p.VideoEncoder.OutputExt) Then
            p.VideoEncoder.Muxer = currentMuxer
        Else
            p.VideoEncoder.Muxer.Init()
        End If

        MainForm.tbTargetFile.Text = p.TargetFile.ChangeExt(p.VideoEncoder.Muxer.OutputExt)
        p.VideoEncoder.OnStateChange()
        MainForm.RecalcBitrate()
        MainForm.Assistant()
    End Sub

    Sub RaiseAppEvent(appEvent As ApplicationEvent)
        For Each i In s.EventCommands
            If i.Enabled AndAlso i.Event = appEvent Then
                Dim matches = 0

                For Each i2 In i.CriteriaList
                    i2.PropertyString = Macro.Solve(i2.Macro)
                    If i2.Eval Then matches += 1
                Next

                If (i.CriteriaList.Count = 0 OrElse (i.OrOnly AndAlso matches > 0) OrElse
                    (Not i.OrOnly AndAlso matches = i.CriteriaList.Count)) AndAlso
                    Not i.CommandParameters Is Nothing Then

                    Log.WriteHeader("Process Event Command '" + i.Name + "'")
                    Log.WriteLine("Event: " + DispNameAttribute.GetValueForEnum(i.Event))
                    Dim command = g.MainForm.CustomMainMenu.CommandManager.GetCommand(i.CommandParameters.MethodName)
                    Log.WriteLine("Command: " + command.MethodInfo.Name)
                    Log.WriteLine(command.GetParameterHelp(i.CommandParameters.Parameters))
                    g.MainForm.CustomMainMenu.CommandManager.Process(i.CommandParameters)
                End If
            End If
        Next
    End Sub

    Sub SetTempDir()
        If p.SourceFile <> "" Then
            p.TempDir = Macro.Solve(p.TempDir)

            If p.TempDir = "" Then
                If FileTypes.VideoOnly.Contains(p.SourceFile.Ext) OrElse
                    FileTypes.VideoText.Contains(p.SourceFile.Ext) OrElse
                    p.SourceFile.Dir.EndsWith("_temp\") Then

                    p.TempDir = p.SourceFile.Dir
                Else
                    Dim base = Filepath.GetBase(p.SourceFile)
                    If base.Length > 60 Then base = base.Shorten(30) + "..."
                    p.TempDir = p.SourceFile.Dir + base + "_temp\"
                End If
            End If

            p.TempDir = p.TempDir.AppendSeparator

            If Not Directory.Exists(p.TempDir) Then
                Try
                    Directory.CreateDirectory(p.TempDir)
                Catch
                    Try
                        p.TempDir = Filepath.GetDirAndBase(p.SourceFile) + "_temp\"
                        If Not Directory.Exists(p.TempDir) Then Directory.CreateDirectory(p.TempDir)
                    Catch
                        MsgWarn("Failed to create a temp directory. By default it's created in the directory of the source file so it's not possible to open files directly from a optical drive unless a temp directory is defined in the options. Usually discs are copied to the hard drive first using a application like MakeMKV, DVDfab or AnyDVD.")
                        Throw New AbortException
                    End Try
                End Try
            End If
        End If
    End Sub

    Function IsProjectDirty() As Boolean
        Return ObjectHelp.GetCompareString(g.SavedProject) <> ObjectHelp.GetCompareString(p)
    End Function

    Sub ShowCommandLinePreview(value As String)
        Using f As New StringEditorForm
            f.tb.ReadOnly = True
            f.cbWrap.Checked = Not value.Contains(BR)
            f.tb.Text = value
            f.tb.SelectionStart = 0
            f.tb.SelectionLength = 0
            f.Text = "Command Line"
            f.Width = 1000
            f.Height = 500
            f.bOK.Visible = False
            f.bCancel.Text = "Close"
            f.ShowDialog()
        End Using
    End Sub

    Sub ffmsindex(sourcePath As String,
                  cachePath As String,
                  Optional indexAudio As Boolean = False,
                  Optional noLog As Boolean = False)

        If File.Exists(sourcePath) AndAlso Not File.Exists(cachePath) AndAlso
            Not FileTypes.VideoText.Contains(Filepath.GetExt(sourcePath)) Then

            Using proc As New Proc
                proc.NoLog = noLog
                proc.Init("Index with ffmsindex", "Indexing, please wait...")
                proc.File = Package.ffms2.GetDir + "ffmsindex.exe"
                proc.Arguments = If(indexAudio, "-t -1 ", "") + """" + sourcePath + """ """ + cachePath + """"
                proc.Start()
            End Using
        End If
    End Sub

    Function IsValidSource(Optional warn As Boolean = True) As Boolean
        If p.SourceScript.GetFrames = 0 Then
            If warn Then
                MsgWarn("Failed to load source.")
            End If

            Return False
        End If

        If Not p.SourceScript.GetErrorMessage Is Nothing Then
            MsgError(p.SourceScript.GetErrorMessage)
            Return False
        End If

        Return True
    End Function

    Function IsSourceSameOrSimilar(path As String) As Boolean
        Return IsSourceSame(path) OrElse IsSourceSimilar(path)
    End Function

    Function IsCOMObjectRegistered(guid As String) As Boolean
        Return File.Exists(Registry.ClassesRoot.GetString("CLSID\" + guid + "\InprocServer32", Nothing))
    End Function

    Function IsSourceSame(path As String) As Boolean
        Return Filepath.GetBase(path).StartsWith(Filepath.GetBase(p.SourceFile))
    End Function

    Function GetFilesInTempDirAndParent() As List(Of String)
        Dim ret As New List(Of String)

        If p.TempDir <> "" Then ret.AddRange(Directory.GetFiles(p.TempDir))

        If p.TempDir <> Filepath.GetDir(p.FirstOriginalSourceFile) Then
            ret.AddRange(Directory.GetFiles(Filepath.GetDir(p.FirstOriginalSourceFile)))
        End If

        Return ret
    End Function

    Function IsSourceSimilar(path As String) As Boolean
        If p.SourceFile.Contains("_") Then
            Dim src = Filepath.GetBase(p.SourceFile)

            While src.Length > 2 AndAlso src.ToCharArray.Last.IsDigit
                src = src.DeleteRight(1)
            End While

            If src.EndsWith("_") AndAlso Filepath.GetBase(path).StartsWith(src.TrimEnd("_"c)) Then
                Return True
            End If
        End If
    End Function

    Function IsCulture(twoLetterCode As String) As Boolean
        Return CultureInfo.CurrentCulture.TwoLetterISOLanguageName = twoLetterCode
    End Function

    Private ExceptionHandled As Boolean

    Sub OnException(ex As Exception)
        If ExceptionHandled Then
            Exit Sub
        Else
            ExceptionHandled = True
        End If

        If TypeOf ex Is AbortException Then
            ProcessForm.CloseProcessForm()
        Else
            Try
                If File.Exists(p.SourceFile) Then
                    Dim name = Filepath.GetBase(p.TargetFile)

                    If name = "" Then
                        name = Filepath.GetBase(p.SourceFile)
                    End If

                    Dim path = Filepath.GetDir(p.SourceFile) + "crash.srip"
                    g.MainForm.SaveProjectPath(path)
                End If

                g.SaveSettings()
            Catch
            End Try

            Try
                ShowException(ex, "Exception")
                MakeBugReport(ex)
            Catch
                g.ShowException(ex)
            End Try

            Process.GetCurrentProcess.Kill()
        End If
    End Sub

    Sub ShowException(e As Exception, Optional msg As String = Nothing, Optional timeout As Integer = 0)
        Try
            Using td As New TaskDialog(Of String)
                If msg = "" Then
                    If TypeOf e Is ErrorAbortException Then
                        td.MainInstruction = DirectCast(e, ErrorAbortException).Title
                    Else
                        td.MainInstruction = e.GetType.Name
                    End If
                Else
                    td.MainInstruction = msg
                End If

                td.Timeout = timeout
                td.Content = e.Message
                td.MainIcon = TaskDialogIcon.Error
                td.ExpandedInformation = e.ToString
                td.Footer = Strings.TaskDialogFooter
                td.Show()
            End Using
        Catch
            Dim title As String

            If TypeOf e Is ErrorAbortException Then
                title = DirectCast(e, ErrorAbortException).Title
            Else
                title = e.GetType.Name
            End If

            VB6.MsgBox(title + BR2 + e.Message + BR2 + e.ToString, VB6.MsgBoxStyle.Critical)
        End Try
    End Sub

    Sub SetRenderer(ms As MenuStrip)
        If VisualStyleInformation.IsEnabledByUser Then
            If s.ToolStripRenderMode = ToolStripRenderMode.Professional Then
                ms.Renderer = New ToolStripProfessionalRenderer()
            Else
                ms.Renderer = New ToolStripRendererEx(s.ToolStripRenderMode)
            End If
        Else
            ms.Renderer = New ToolStripSystemRenderer()
        End If

        ToolStripManager.Renderer = ms.Renderer
    End Sub

    Sub Play(file As String, Optional cliOptions As String = Nothing)
        If Package.MPC.VerifyOK(True) Then
            Dim args = """" + file + """"
            If cliOptions <> "" Then args += " " + cliOptions
            g.ShellExecute(Package.MPC.Path, args)
        End If
    End Sub

    Sub ShellExecute(cmd As String, Optional args As String = Nothing)
        Try
            Process.Start(cmd, args)
        Catch ex As Exception
            If cmd Like "http*://*" Then
                MsgError("Failed to open URL with browser." + BR2 + cmd, ex.Message)
            ElseIf File.Exists(cmd) Then
                MsgError("Failed to launch file." + BR2 + cmd, ex.Message)
            ElseIf Directory.Exists(cmd) Then
                MsgError("Failed to launch directory." + BR2 + cmd, ex.Message)
            Else
                g.ShowException(ex, "Failed to execute command:" + BR2 + cmd + BR2 + "Arguments:" + BR2 + args)
            End If
        End Try
    End Sub

    Sub OpenDirAndSelectFile(filepath As String, handle As IntPtr)
        If File.Exists(filepath) Then
            g.ShellExecute(StaxRip.Filepath.GetDir(filepath))

            Try
                For x = 0 To 9
                    Thread.Sleep(300)
                    Application.DoEvents()

                    If handle <> Native.GetForegroundWindow Then
                        Explorer.SelectFile(Native.GetForegroundWindow, filepath)
                        Exit For
                    End If
                Next
            Catch
            End Try
        ElseIf Directory.Exists(StaxRip.Filepath.GetDir(filepath)) Then
            g.ShellExecute(StaxRip.Filepath.GetDir(filepath))
        End If
    End Sub

    Sub OnUnhandledException(sender As Object, e As ThreadExceptionEventArgs)
        OnException(e.Exception)
    End Sub

    Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        OnException(DirectCast(e.ExceptionObject, Exception))
    End Sub

    Sub MakeBugReport(e As Exception)
        If e Is Nothing AndAlso Not g.IsValidSource(False) Then
            MsgWarn("Making a bug report requires to have a project encoded or at least a source file opened.")
            Exit Sub
        End If

        If MsgQuestion("Would you like to submit the log file?",
                       "Please submit the log file via mail helping to fix the problem.") <> DialogResult.OK Then
            Exit Sub
        End If

        If Not e Is Nothing Then
            SyncLock p.Log
                If p.Log.Length = 0 Then Log.WriteEnvironment()
            End SyncLock

            Log.WriteHeader("Exception")
            Log.WriteLine(e.ToString)
        End If

        Dim fp = If(File.Exists(p.SourceFile) AndAlso Directory.Exists(p.TempDir),
                    p.TempDir + p.Name + "_StaxRip.log", Folder.Settings + "Log.txt")

        SyncLock p.Log
            p.Log.ToString.WriteANSIFile(fp)
        End SyncLock

        g.OpenDirAndSelectFile(fp, g.MainForm.Handle)
        g.ShellExecute(g.GetTextEditor(), """" + fp + """")
        g.ShellExecute("mailto:frank.skare.de@gmail.com?subject=StaxRip%20feedback&body=please%20paste%20the%20log%20file%20content%20here")
    End Sub

    Function WasFileJustWritten(path As String) As Boolean
        For x = 0 To 50
            If File.Exists(path) Then Return True
            Thread.Sleep(1000)
        Next
    End Function

    Sub ShutdownPC()
        ShutdownPC(CType(Registry.CurrentUser.GetInt("Software\" + Application.ProductName, "ShutdownMode"), ShutdownMode))
    End Sub

    Sub ShutdownPC(mode As ShutdownMode)
        If mode <> ShutdownMode.Nothing Then
            SavedProject = p
            g.MainForm.Close()

            If Process.GetProcessesByName("StaxRip").Length = 1 Then
                Registry.CurrentUser.Write("Software\" + Application.ProductName, "ShutdownMode", 0)
                Shutdown.Commit(mode)
            End If
        End If
    End Sub

    Sub Highlight(highlight As Boolean, c As Control)
        If highlight Then
            c.BackColor = Color.Orange
        Else
            If TypeOf c Is Label OrElse TypeOf c Is GroupBox Then
                c.BackColor = SystemColors.Control
            ElseIf TypeOf c Is TextBox AndAlso DirectCast(c, TextBox).ReadOnly Then
                c.BackColor = SystemColors.Control
            Else
                c.BackColor = SystemColors.Window
            End If
        End If
    End Sub

    Function EnableFilter(cat As String) As Boolean
        For Each i In p.Script.Filters
            If i.Category = cat Then
                If Not i.Active Then
                    i.Active = True
                    g.MainForm.AviSynthListView.Load()
                End If

                Return True
            End If
        Next
    End Function

    Function BrowseFile(filter As String, Optional defaultFilepath As String = Nothing) As String
        Using d As New OpenFileDialog
            d.Filter = filter

            If File.Exists(defaultFilepath) Then
                d.InitialDirectory = Filepath.GetDir(defaultFilepath)
                d.FileName = Filepath.GetName(defaultFilepath)
            End If

            If d.ShowDialog = DialogResult.OK Then
                Return d.FileName
            End If
        End Using
    End Function

    Sub ShowDirectShowWarning()
        If Not p.BatchMode Then
            If Not g.IsCOMObjectRegistered(GUIDS.LAVSplitter) OrElse
                Not g.IsCOMObjectRegistered(GUIDS.LAVVideoDecoder) Then

                MsgError("DirectShow Filter Setup",
                         "An error occurred that could possibly be solved by installing [http://code.google.com/p/lavfilters LAV Filters].")
            End If
        End If
    End Sub

    Sub AddHardcodedSubtitle()
        For Each subtitle In p.VideoEncoder.Muxer.Subtitles
            If subtitle.Path.Ext.EqualsAny("srt", "ass", "idx") Then
                subtitle.Enabled = False
                p.AddHardcodedSubtitleFilter(subtitle.Path)
            End If
        Next
    End Sub

    Sub RunAutoCrop()
        p.SourceScript.Synchronize(True)

        Using avi As New AVIFile(p.SourceScript.Path)
            Dim segmentCount = 20

            Dim len = avi.FrameCount \ (segmentCount + 1)
            Dim crops(segmentCount - 1) As AutoCrop

            For x = 1 To segmentCount
                avi.Position = len * x

                Using bmp = avi.GetBitmap
                    crops(x - 1) = AutoCrop.Start(bmp.Clone(New Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppRgb), avi.Position)
                End Using
            Next

            Dim leftCrops = crops.SelectMany(Function(arg) arg.Left).OrderBy(Function(arg) arg)
            p.CropLeft = leftCrops(leftCrops.Count \ 10)

            Dim topCrops = crops.SelectMany(Function(arg) arg.Top).OrderBy(Function(arg) arg)
            p.CropTop = topCrops(topCrops.Count \ 10)

            Dim rightCrops = crops.SelectMany(Function(arg) arg.Right).OrderBy(Function(arg) arg)
            p.CropRight = rightCrops(rightCrops.Count \ 10)

            Dim bottomCrops = crops.SelectMany(Function(arg) arg.Bottom).OrderBy(Function(arg) arg)
            p.CropBottom = bottomCrops(bottomCrops.Count \ 10)

            CorrectCropMod()
        End Using
    End Sub

    Sub SmartCrop()
        If Not p.Script.IsFilterActive("Resize") Then
            Exit Sub
        End If

        Dim tempLeft = p.CropLeft
        Dim tempRight = p.CropRight

        Dim ae = Math.Abs(Calc.GetAspectRatioError)

        While (p.SourceWidth - p.CropLeft - p.CropRight) > 64
            p.CropLeft += 2
            p.CropRight += 2

            If Math.Abs(Calc.GetAspectRatioError()) < ae Then
                tempLeft = p.CropLeft
                tempRight = p.CropRight
                ae = Math.Abs(Calc.GetAspectRatioError())
            End If
        End While

        p.CropLeft = tempLeft
        p.CropRight = tempRight

        Dim tempTop = p.CropTop
        Dim tempBottom = p.CropBottom

        While (p.SourceHeight - p.CropTop - p.CropBottom) > 64
            p.CropTop += 2
            p.CropBottom += 2

            If Math.Abs(Calc.GetAspectRatioError()) < ae Then
                tempTop = p.CropTop
                tempBottom = p.CropBottom
                ae = Math.Abs(Calc.GetAspectRatioError())
            End If
        End While

        p.CropTop = tempTop
        p.CropBottom = tempBottom
    End Sub

    Sub OvercropWidth()
        If p.AutoSmartOvercrop > 0 AndAlso
            p.AutoSmartOvercrop < Calc.GetTargetDAR Then

            Dim newar = p.AutoSmartOvercrop
            Dim croph = p.SourceHeight - p.CropTop - p.CropBottom
            g.MainForm.tbTargetHeight.Text = Calc.FixMod16(CInt(p.TargetWidth / newar)).ToString
            newar = CSng(p.TargetWidth / p.TargetHeight)
            Dim cropw = (newar / Calc.GetSourceDAR * (croph / p.SourceHeight)) * p.SourceWidth
            p.CropLeft = CInt((p.SourceWidth - cropw) / 2)
            p.CropRight = p.CropLeft

            If p.CropLeft < 0 Then
                p.CropLeft = 0
                p.CropRight = 0
            End If

            CorrectCropMod()
        End If
    End Sub

    Function ConvertPath(value As String) As String
        If value.Contains("Constant Quality") Then
            value = value.Replace("Constant Quality", "CQ")
        End If

        If value.Contains("Misc | ") Then
            value = value.Replace("Misc | ", "")
        End If

        If value.Contains("Advanced | ") Then
            value = value.Replace("Advanced | ", "")
        End If

        If value.Contains(" | ") Then
            value = value.Replace(" | ", " - ")
        End If

        If value.Contains("  ") Then
            value = value.Replace("  ", " ")
        End If

        Return value
    End Function

    Sub CorrectCropMod()
        CorrectCropMod(False)
    End Sub

    Sub ForceCropMod()
        If Not g.EnableFilter("Crop") Then
            p.Script.InsertAfter("Source", New VideoFilter("Crop", "Crop", "Crop(%crop_left%, %crop_top%, -%crop_right%, -%crop_bottom%)"))
        End If

        CorrectCropMod(True)
    End Sub

    Private Sub CorrectCropMod(force As Boolean)
        If p.AutoCorrectCropValues OrElse force Then
            p.CropLeft += p.CropLeft Mod 2
            p.CropRight += p.CropRight Mod 2
            p.CropTop += p.CropTop Mod 2
            p.CropBottom += p.CropBottom Mod 2

            Dim modValue = 4

            If Not p.Script.IsFilterActive("Resize") Then
                modValue = p.ForcedOutputMod
            End If

            Dim whalf = ((p.SourceWidth - p.CropLeft - p.CropRight) Mod modValue) \ 2

            If p.CropLeft > p.CropRight Then
                p.CropLeft += whalf - whalf Mod 2
                p.CropRight += whalf + whalf Mod 2
            Else
                p.CropRight += whalf - whalf Mod 2
                p.CropLeft += whalf + whalf Mod 2
            End If

            Dim hhalf = ((p.SourceHeight - p.CropTop - p.CropBottom) Mod modValue) \ 2

            If p.CropTop > p.CropBottom Then
                p.CropTop += hhalf - hhalf Mod 2
                p.CropBottom += hhalf + hhalf Mod 2
            Else
                p.CropBottom += hhalf - hhalf Mod 2
                p.CropTop += hhalf + hhalf Mod 2
            End If

            g.MainForm.AviSynthListView.Load()
        End If
    End Sub
End Class

Public Enum AudioMode
    DirectStreamCopy
    FullProcessingMode
End Enum

Public Enum RegistryRoot
    CurrentUser
    LocalMachine
    ClassesRoot
End Enum

<Serializable()>
Public Class Range
    Implements IComparable(Of Range)

    Public Start As Integer
    Public [End] As Integer

    Sub New(startPosition As Integer, endPosition As Integer)
        Me.Start = startPosition
        Me.End = endPosition
    End Sub

    Function GetLenght() As Integer
        Return Me.End - Start
    End Function

    Function CompareTo(other As Range) As Integer Implements System.IComparable(Of Range).CompareTo
        Return Start.CompareTo(other.Start)
    End Function
End Class

Class Log
    Shared StartTime As DateTime

    Shared Event Update(text As String)

    Shared Sub Write(title As String, content As String)
        StartTime = DateTime.Now

        SyncLock p.Log
            If Not p.Log.ToString.EndsWith(BR2) Then p.Log.AppendLine()
            p.Log.Append(FormatHeader(title))
        End SyncLock

        If content <> "" Then
            If content.EndsWith(BR) Then
                SyncLock p.Log
                    p.Log.Append(content)
                End SyncLock
            Else
                SyncLock p.Log
                    p.Log.AppendLine(content)
                End SyncLock
            End If
        End If

        RaiseUpdate()
    End Sub

    Shared Sub WriteHeader(value As String)
        StartTime = DateTime.Now

        If value <> "" Then
            SyncLock p.Log
                If Not p.Log.ToString.EndsWith(BR2) Then p.Log.AppendLine()
                p.Log.Append(FormatHeader(value))
            End SyncLock

            RaiseUpdate()
        End If
    End Sub

    'TODO: hide when bug is found
    Shared Sub Debug(title As String, value As String, Optional requiredContent As String = Nothing)
        If requiredContent <> "" AndAlso Not p.Log.ToString.Contains(requiredContent) Then Exit Sub
        Write(title, value)
    End Sub

    Shared Sub WriteLine(value As String)
        If value <> "" Then
            If value.EndsWith(BR) Then
                SyncLock p.Log
                    p.Log.Append(value)
                End SyncLock
            Else
                SyncLock p.Log
                    p.Log.AppendLine(value)
                End SyncLock
            End If

            RaiseUpdate()
        End If
    End Sub

    Shared Function FormatHeader(value As String) As String
        Return "-=".Multiply(30) + "-" + BR +
            value.PadLeft(30 + value.Length \ 2) +
            BR + "-=".Multiply(30) + "-" + BR2
    End Function

    Shared Sub WriteEnvironment()
        If p.Log.ToString.Contains("Environment" + BR + "-=") Then Exit Sub

        Dim staxrip = "-=".Multiply(30) + "-" + BR +
"      _________ __                __________.__        
     /   _____//  |______  ___  __\______   \__|_____  
     \_____  \\   __\__  \ \  \/  /|       _/  \____ \ 
     /        \|  |  / __ \_>    < |    |   \  |  |_> >
    /_______  /|__| (____  /__/\_ \|____|_  /__|   __/ 
            \/           \/      \/       \/   |__|   "

        WriteLine(staxrip)
        WriteHeader("Environment")

        Dim mc As New ManagementClass("Win32_VideoController")
        Dim videoControllerCaptions = From i2 In mc.GetInstances().OfType(Of ManagementBaseObject)() Select CStr(i2("Caption"))

        Dim temp =
            "StaxRip x64: " + Application.ProductVersion + BR +
            "OS: " + Registry.LocalMachine.GetString("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") + BR +
            "Language: " + CultureInfo.CurrentCulture.EnglishName + BR +
            "CPU: " + Registry.LocalMachine.GetString("HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") + BR +
            "GPU: " + String.Join(", ", videoControllerCaptions)

        WriteLine(temp.FormatColumn(":"))
    End Sub

    Shared Sub Save()
        If p.SourceFile <> "" Then
            SyncLock p.Log
                If Directory.Exists(p.TempDir) Then
                    p.Log.ToString.WriteFile(p.TempDir + p.Name + "_StaxRip.log", Encoding.UTF8)
                End If
            End SyncLock
        End If
    End Sub

    Shared Sub WriteStats()
        WriteStats(StartTime)
    End Sub

    Shared Sub WriteStats(start As DateTime)
        Dim n = DateTime.Now.Subtract(start)

        SyncLock p.Log
            If Not p.Log.ToString.EndsWith(BR2) Then p.Log.AppendLine()
            p.Log.Append("Start: ".PadRight(10) + start.ToLongTimeString + BR)
            p.Log.Append("End: ".PadRight(10) + DateTime.Now.ToLongTimeString + BR)
            p.Log.Append("Duration: " + CInt(Math.Floor(n.TotalHours)).ToString("d2") + ":" + n.Minutes.ToString("d2") + ":" + n.Seconds.ToString("d2") + BR)
            p.Log.AppendLine()
        End SyncLock

        RaiseUpdate()
    End Sub

    Private Shared Sub RaiseUpdate()
        SyncLock p.Log
            RaiseEvent Update(p.Log.ToString)
        End SyncLock
    End Sub
End Class

Class Calc
    Shared Function GetYFromTwoPointForm(x1 As Single, y1 As Single, x2 As Single, y2 As Single, x As Single) As Integer
        'Zweipunkteform nach y aufgel�st
        Return CInt((((y2 - y1) / (x2 - x1)) * (x - x1)) + y1)
    End Function

    Shared Function GetPercent() As Double
        If p.Compressibility = 0 Then Return 0
        Return (GetBPF() / p.Compressibility) * 100
    End Function

    Shared Function GetBPF() As Double
        Dim framerate = p.Script.GetFramerate

        If framerate = 0 Then Return 0
        If p.TargetWidth = 0 Then Return 0
        If p.TargetHeight = 0 Then Return 0

        Return p.VideoBitrate * 1024L / (p.TargetWidth * p.TargetHeight * CLng(framerate))
    End Function

    Shared Function GetSize() As Double
        Dim ret = (Calc.GetVideoKBytes() + Calc.GetAudioKBytes() +
            GetSubtitleKBytes() + Calc.GetOverheadKBytes()) / 1024

        If ret < 1 Then ret = 1
        Return ret
    End Function

    Shared Function GetVideoBitrate() As Double
        If p.TargetSeconds = 0 Then Return 0
        Dim kbytes = p.TargetSize * 1024 - GetAudioKBytes() - GetSubtitleKBytes() - GetOverheadKBytes()
        Dim ret = kbytes * 8 * 1.024 / p.TargetSeconds
        If ret < 1 Then ret = 1
        Return ret
    End Function

    Shared Function GetVideoKBytes() As Double
        Return ((p.VideoBitrate * p.TargetSeconds) / 8) / 1.024
    End Function

    Shared Function GetSubtitleKBytes() As Double
        Return Aggregate i In p.VideoEncoder.Muxer.Subtitles Into Sum(If(i.Enabled, i.Size / 1024 / 3, 0))
    End Function

    Shared Function GetOverheadKBytes() As Double
        Dim ret As Double
        Dim frames = p.Script.GetFrames

        If {"avi", "divx"}.Contains(p.VideoEncoder.Muxer.OutputExt) Then
            ret += frames * 0.024
            If p.Audio0.File <> "" Then ret += frames * 0.04
            If p.Audio1.File <> "" Then ret += frames * 0.04
        ElseIf p.VideoEncoder.Muxer.OutputExt = "mp4" Then
            ret += 10.4 / 1024 * frames
        ElseIf p.VideoEncoder.Muxer.OutputExt = "mkv" Then
            ret += frames * 0.013
        End If

        Return ret
    End Function

    Shared Function GetAudioKBytes() As Double
        Return ((Calc.GetAudioBitrate() * p.TargetSeconds) / 8) / 1.024
    End Function

    Shared Function GetAudioBitrate() As Double
        Dim b0, b1 As Double

        If p.Audio0.File <> "" Then b0 = p.Audio0.Bitrate
        If p.Audio1.File <> "" Then b1 = p.Audio1.Bitrate

        Return b0 + b1 + p.AudioTracks.Sum(Function(arg) arg.Bitrate)
    End Function

    Shared Function GetBitrateFromFile(path As String, seconds As Integer) As Double
        Try
            If path = "" OrElse seconds = 0 Then Return 0
            Dim kBits = New FileInfo(path).Length * 8 / 1000
            Return kBits / seconds
        Catch ex As Exception
            g.ShowException(ex)
        End Try
    End Function

    Shared Function IsARSignalingRequired() As Boolean
        If Not p.Script Is Nothing AndAlso p.AutoARSignaling Then
            Dim par = GetTargetPAR()

            If par.X <> par.Y Then
                If p.Script.IsFilterActive("Resize") Then
                    Return Math.Abs(GetAspectRatioError()) > p.MaxAspectRatioError
                Else
                    Return True
                End If
            End If
        End If
    End Function

    Private Shared Function GetSimplePar(par As Point) As Point
        If par.Y > 0 Then
            For Each i In New Point() {New Point(12, 11), New Point(10, 11), New Point(16, 11), New Point(40, 33)}
                If Math.Abs((par.X / par.Y) / (i.X / i.Y) * 100 - 100) < 1 Then
                    Return i
                End If
            Next
        End If

        If par.X > 255 OrElse par.Y > 255 Then
            Dim x = par.X / 255
            Dim y = par.Y / 255

            If x > y Then
                par.X = CInt(par.X / x)
                par.Y = CInt(par.Y / x)
            Else
                par.X = CInt(par.X / y)
                par.Y = CInt(par.Y / y)
            End If
        End If

        If par.X = par.Y Then
            par.X = 1
            par.Y = 1
        End If

        Return par
    End Function

    Shared Function ParseCustomAR(value As String) As Point
        If value.Contains(":") OrElse value.Contains("/") Then
            Dim a = value.Split(":/".ToCharArray)

            If a.Length = 2 AndAlso a(0).IsInt AndAlso a(1).IsInt Then
                Return New Point(a(0).ToInt, a(1).ToInt)
            End If
        ElseIf Double.TryParse(value, Nothing) Then
            Return New Point(CInt(CDbl(value) * 100000), 100000)
        End If
    End Function

    Shared Function GetSourceDAR() As Double
        Try
            Dim par = GetSourcePAR()
            Return (par.X * p.SourceWidth) / (par.Y * p.SourceHeight)
        Catch ex As Exception
            Return 4 / 3
        End Try
    End Function

    Shared Function GetSimpleSourcePAR() As Point
        Return GetSimplePar(GetSourcePAR)
    End Function

    Shared Function GetSourcePAR() As Point
        If p.CustomPAR <> "" Then Return Reduce(ParseCustomAR(p.CustomPAR))

        If p.CustomDAR <> "" Then
            Dim r = ParseCustomAR(p.CustomDAR)
            Return Reduce(New Point(p.SourceHeight * r.X, p.SourceWidth * r.Y))
        End If

        Dim par As New Point(1, 1)
        Dim w = p.SourceWidth, h = p.SourceHeight

        If (h = 576 OrElse h = 480) AndAlso w <= 768 Then
            Dim f As VideoFormat

            For Each i In Formats
                If i.Width = p.SourceWidth AndAlso i.Height = p.SourceHeight Then
                    f = i
                    Exit For
                End If
            Next

            If f.Width > 0 Then
                Dim samplingWidth = 52.0

                If Not p.ITU Then
                    samplingWidth = f.Width / f.SamplingRate
                End If

                Dim dar = (p.SourcePAR.X * p.SourceWidth) / (p.SourcePAR.Y * p.SourceHeight)

                par.X = CInt(If(p.SourceAnamorphic OrElse dar > 1.7, 16 / 9, 4 / 3) * f.Height)
                par.Y = CInt(f.SamplingRate * samplingWidth)

                Return Reduce(par)
            Else
                Dim dar = If(p.SourceAnamorphic, 16 / 9, 4 / 3)

                If p.ITU Then
                    dar *= 1.0255
                End If

                par.X = CInt(dar * h)
                par.Y = CInt(w)

                Return Reduce(par)
            End If
        End If

        If h = 720 OrElse h = 1080 OrElse h = 1088 Then
            If p.SourcePAR.X = 1364 Then
                p.SourcePAR.X = 4
                p.SourcePAR.Y = 3
            End If
        End If

        Return Reduce(p.SourcePAR)
    End Function

    Shared Function GetTargetPAR() As Point
        Try
            Dim par = GetSourcePAR()

            Dim cw = p.SourceWidth
            Dim ch = p.SourceHeight

            If p.Script.IsFilterActive("Crop") Then
                cw -= p.CropLeft + p.CropRight
                ch -= p.CropTop + p.CropBottom
            End If

            If p.TargetWidth <> cw OrElse p.TargetHeight <> ch Then
                Dim par2 = Reduce(New Point(cw * p.TargetHeight, ch * p.TargetWidth))
                par.X = par.X * par2.X
                par.Y = par.Y * par2.Y
                par = Reduce(par)
            End If

            Return GetSimplePar(par)
        Catch ex As Exception
            Return New Point(1, 1)
        End Try
    End Function

    Shared Function GetTargetDAR() As Double
        Dim w = p.SourceWidth, h = p.SourceHeight
        Dim cropw = w, croph = h

        If p.Script.IsFilterActive("Crop") Then
            cropw = w - p.CropLeft - p.CropRight
            croph = h - p.CropTop - p.CropBottom
        End If

        Return ((cropw / w) / (croph / h)) * GetSourceDAR()
    End Function

    Shared Function GetAspectRatioError() As Double
        Return ((p.TargetWidth / p.TargetHeight) / Calc.GetTargetDAR) * 100 - 100
    End Function

    Shared Function Reduce(p As Point) As Point
        If p.X <> 0 AndAlso p.Y <> 0 Then
            Dim gcd = GetGCD(p.X, p.Y)
            p.X \= gcd
            p.Y \= gcd
        End If

        Return p
    End Function

    Shared Function GetGCD(a As Integer, b As Integer) As Integer
        If b = 0 Then
            Return a
        Else
            Return GetGCD(b, a Mod b)
        End If
    End Function

    Shared Function FixMod16(value As Integer) As Integer
        Return CInt(value / 16) * 16
    End Function

    Shared Function FixMod(value As Integer, modValue As Integer) As Integer
        Return CInt(value / modValue) * modValue
    End Function

    Shared Function GetMod(
        w As Integer,
        h As Integer,
        Optional skip16 As Boolean = True) As String

        Dim wmod, hmod As Integer

        For Each x In {1, 2, 4, 8, 16}
            If w Mod x = 0 Then wmod = x
            If h Mod x = 0 Then hmod = x
        Next

        If wmod = 16 AndAlso hmod = 16 Then
            If skip16 Then
                Return ""
            Else
                Return "16/16"
            End If
        Else
            Dim x = w - FixMod16(w)
            Dim xval As String

            If x = 8 OrElse x = -8 Then
                xval = "�8"
            ElseIf x > 0 Then
                xval = "+" & x
            Else
                xval = x.ToString
            End If

            Dim y = h - FixMod16(h)
            Dim yval As String

            If y = 8 OrElse y = -8 Then
                yval = "�8"
            ElseIf y > 0 Then
                yval = "+" & y
            Else
                yval = y.ToString
            End If

            Return wmod & "/" & hmod & " (" & xval & "/" & yval & ")"
        End If
    End Function

    Shared Function GetNextMod(val As Integer, step1 As Integer) As Integer
        Do
            val += 1
        Loop Until val Mod step1 = 0

        Return val
    End Function

    Shared Function GetPreviousMod(val As Integer, step1 As Integer) As Integer
        Do
            val -= 1
        Loop Until val Mod step1 = 0

        Return val
    End Function

    Private Shared FormatsValue As VideoFormat()

    Shared ReadOnly Property Formats() As VideoFormat()
        Get
            If FormatsValue Is Nothing Then
                FormatsValue = {
                    New VideoFormat(768, 576, 14.75),
                    New VideoFormat(768, 560, 14.75),
                    New VideoFormat(720, 576, 13.5),
                    New VideoFormat(704, 576, 13.5),
                    New VideoFormat(702, 576, 13.5),
                    New VideoFormat(544, 576, 10.125),
                    New VideoFormat(480, 576, 9.0),
                    New VideoFormat(384, 288, 7.375),
                    New VideoFormat(384, 280, 7.375),
                    New VideoFormat(352, 576, 6.75),
                    New VideoFormat(352, 288, 6.75),
                    New VideoFormat(176, 144, 3.375),
                    New VideoFormat(720, 486, 13.5),
                    New VideoFormat(720, 480, 13.5),
                    New VideoFormat(711, 486, 13.5),
                    New VideoFormat(704, 486, 13.5),
                    New VideoFormat(704, 480, 13.5),
                    New VideoFormat(640, 480, 12.27272),
                    New VideoFormat(480, 480, 9.0),
                    New VideoFormat(352, 480, 6.75),
                    New VideoFormat(352, 240, 6.75),
                    New VideoFormat(320, 240, 6.13636)
                }
            End If

            Return FormatsValue
        End Get
    End Property
End Class

Public Structure VideoFormat
    Sub New(width As Integer, height As Integer, samplingRate As Double)
        Me.Width = width
        Me.Height = height
        Me.SamplingRate = samplingRate
    End Sub

    Public Width As Integer
    Public Height As Integer
    Public SamplingRate As Double
End Structure

<Serializable()>
Public Class Language
    Implements IComparable(Of Language)

    <NonSerialized>
    Public IsCommon As Boolean

    Sub New()
        Me.New("")
    End Sub

    Sub New(ci As CultureInfo, Optional isCommon As Boolean = False)
        Me.IsCommon = isCommon
        CultureInfoValue = ci
    End Sub

    Sub New(twoLetterCode As String, Optional isCommon As Boolean = False)
        Try
            Me.IsCommon = isCommon

            Select Case twoLetterCode
                Case "iw"
                    twoLetterCode = "he"
                Case "jp"
                    twoLetterCode = "ja"
            End Select

            CultureInfoValue = New CultureInfo(twoLetterCode)
        Catch ex As Exception
            CultureInfoValue = CultureInfo.InvariantCulture
        End Try
    End Sub

    Private CultureInfoValue As CultureInfo

    ReadOnly Property CultureInfo() As CultureInfo
        Get
            Return CultureInfoValue
        End Get
    End Property

    ReadOnly Property TwoLetterCode() As String
        Get
            Return CultureInfo.TwoLetterISOLanguageName
        End Get
    End Property

    <NonSerialized()> Private ThreeLetterCodeValue As String

    ReadOnly Property ThreeLetterCode() As String
        Get
            If ThreeLetterCodeValue Is Nothing Then
                If CultureInfo.TwoLetterISOLanguageName = "iv" Then
                    ThreeLetterCodeValue = "und"
                Else
                    Select Case CultureInfo.ThreeLetterISOLanguageName
                        Case "deu"
                            ThreeLetterCodeValue = "ger"
                        Case "ces"
                            ThreeLetterCodeValue = "cze"
                        Case "zho"
                            ThreeLetterCodeValue = "chi"
                        Case "nld"
                            ThreeLetterCodeValue = "dut"
                        Case "ell"
                            ThreeLetterCodeValue = "gre"
                        Case "fra"
                            ThreeLetterCodeValue = "fre"
                        Case "sqi"
                            ThreeLetterCodeValue = "alb"
                        Case "hye"
                            ThreeLetterCodeValue = "arm"
                        Case "eus"
                            ThreeLetterCodeValue = "baq"
                        Case "mya"
                            ThreeLetterCodeValue = "bur"
                        Case "kat"
                            ThreeLetterCodeValue = "geo"
                        Case "isl"
                            ThreeLetterCodeValue = "ice"
                        Case "bng"
                            ThreeLetterCodeValue = "ben"
                        Case Else
                            ThreeLetterCodeValue = CultureInfo.ThreeLetterISOLanguageName
                    End Select
                End If
            End If

            Return ThreeLetterCodeValue
        End Get
    End Property

    ReadOnly Property Name() As String
        Get
            If CultureInfo.TwoLetterISOLanguageName = "iv" Then
                Return "Undetermined"
            Else
                Return CultureInfo.EnglishName
            End If
        End Get
    End Property

    Private Shared LanguagesValue As List(Of Language)

    Shared ReadOnly Property Languages() As List(Of Language)
        Get
            If LanguagesValue Is Nothing Then
                Dim l As New List(Of Language)

                l.Add(New Language("en", True))
                l.Add(New Language("es", True))
                l.Add(New Language("de", True))
                l.Add(New Language("fr", True))
                l.Add(New Language("it", True))
                l.Add(New Language("ru", True))
                l.Add(New Language("zh", True))
                l.Add(New Language("hi", True))
                l.Add(New Language("ja", True))
                l.Add(New Language("pt", True))
                l.Add(New Language("ar", True))
                l.Add(New Language("bn", True))
                l.Add(New Language("pa", True))
                l.Add(New Language("ms", True))
                l.Add(New Language("ko", True))

                l.Add(New Language(CultureInfo.InvariantCulture, True))

                Dim current = l.Where(Function(a) a.TwoLetterCode = CultureInfo.CurrentCulture.TwoLetterISOLanguageName).FirstOrDefault

                If current Is Nothing Then l.Add(CurrentCulture)

                l.Sort()

                Dim l2 As New List(Of Language)

                For Each i In CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                    l2.Add(New Language(i))
                Next

                l2.Sort()
                l.AddRange(l2)
                LanguagesValue = l
            End If

            Return LanguagesValue
        End Get
    End Property

    Shared ReadOnly Property CurrentCulture As Language
        Get
            Return New Language(CultureInfo.CurrentCulture.NeutralCulture, True)
        End Get
    End Property


    Overrides Function ToString() As String
        Return Name
    End Function

    Function CompareTo(other As Language) As Integer Implements System.IComparable(Of Language).CompareTo
        Return Name.CompareTo(other.Name)
    End Function

    Overrides Function Equals(o As Object) As Boolean
        If TypeOf o Is Language Then
            Return CultureInfo.Equals(DirectCast(o, Language).CultureInfo)
        End If
    End Function
End Class

Class CmdlTypeEditor
    Inherits UITypeEditor

    Overloads Overrides Function EditValue(context As ITypeDescriptorContext,
                                           provider As IServiceProvider,
                                           value As Object) As Object
        Using f As New MacroEditor
            f.SetBatchDefaults()
            f.MacroEditorControl.Value = CStr(value)

            If f.ShowDialog = DialogResult.OK Then
                Return f.MacroEditorControl.Value
            Else
                Return value
            End If
        End Using
    End Function

    Overloads Overrides Function GetEditStyle(context As ITypeDescriptorContext) As UITypeEditorEditStyle
        Return UITypeEditorEditStyle.Modal
    End Function
End Class

Class ScriptTypeEditor
    Inherits UITypeEditor

    Overloads Overrides Function EditValue(context As ITypeDescriptorContext, provider As IServiceProvider, value As Object) As Object
        Using f As New MacroEditor
            f.SetScriptDefaults()
            f.MacroEditorControl.Value = CStr(value)

            If f.ShowDialog = DialogResult.OK Then
                Return f.MacroEditorControl.Value
            Else
                Return value
            End If
        End Using
    End Function

    Overloads Overrides Function GetEditStyle(context As ITypeDescriptorContext) As UITypeEditorEditStyle
        Return UITypeEditorEditStyle.Modal
    End Function
End Class

Class MacroStringTypeEditor
    Inherits UITypeEditor

    Overloads Overrides Function EditValue(context As ITypeDescriptorContext,
                                           provider As IServiceProvider,
                                           value As Object) As Object
        Using f As New MacroEditor
            f.SetMacroDefaults()
            f.MacroEditorControl.Value = CStr(value)

            If f.ShowDialog = DialogResult.OK Then
                Return f.MacroEditorControl.Value
            Else
                Return value
            End If
        End Using
    End Function

    Overloads Overrides Function GetEditStyle(context As ITypeDescriptorContext) As UITypeEditorEditStyle
        Return UITypeEditorEditStyle.Modal
    End Function
End Class

<Serializable()>
Public MustInherit Class Profile
    Implements IComparable(Of Profile)

    Sub New()
    End Sub

    Sub New(name As String)
        Me.Name = name
    End Sub

    Private NameValue As String

    Overridable Property Name() As String
        Get
            If NameValue = "" Then Return DefaultName
            Return NameValue
        End Get
        Set(Value As String)
            If Value = DefaultName Then
                NameValue = Nothing
            Else
                NameValue = Value
            End If
        End Set
    End Property

    Overridable ReadOnly Property DefaultName As String
        Get
            Return "untitled"
        End Get
    End Property

    Protected CanEditValue As Boolean

    Overridable ReadOnly Property CanEdit() As Boolean
        Get
            Return CanEditValue
        End Get
    End Property

    Overridable Function Edit() As DialogResult
    End Function

    Overridable Function CreateEditControl() As Control
        Return Nothing
    End Function

    Overridable Sub Clean()
    End Sub

    Overridable Function GetCopy() As Profile
        Return DirectCast(ObjectHelp.GetCopy(Me), Profile)
    End Function

    Overrides Function ToString() As String
        Return Name
    End Function

    Function CompareTo(other As Profile) As Integer Implements System.IComparable(Of Profile).CompareTo
        Return Name.CompareTo(other.Name)
    End Function
End Class

<Serializable()>
Class Macro
    Implements IComparable(Of Macro)

    Sub New()
        MyClass.New("", "", GetType(String), "")
    End Sub

    Sub New(name As String,
            friendlyName As String,
            type As Type,
            description As String)

        If name.StartsWith("$") Then
            Me.Name = name
        Else
            Me.Name = "%" + name + "%"
        End If

        Me.FriendlyName = friendlyName
        Me.Type = type
        Me.Description = description
    End Sub

    Private NameValue As String

    Property Name() As String
        Get
            If NameValue Is Nothing Then NameValue = ""
            Return NameValue
        End Get
        Set(Value As String)
            If (Not Value.StartsWith("%") AndAlso Not Value.StartsWith("$")) OrElse
                (Not Value.EndsWith("%") AndAlso Not Value.EndsWith("$")) Then

                Throw New Exception("Macro must start and end with '%' or '$'")
            End If

            NameValue = Value
        End Set
    End Property

    Private FriendlyNameValue As String

    Property FriendlyName() As String
        Get
            If FriendlyNameValue = "" AndAlso NameValue <> "" Then
                FriendlyNameValue = NameValue.Replace("_", " ").Replace("%", " ").Trim(" "c).ToTitleCase
            End If

            If FriendlyNameValue Is Nothing Then FriendlyNameValue = ""

            Return FriendlyNameValue
        End Get
        Set(Value As String)
            FriendlyNameValue = Value
        End Set
    End Property

    Private TypeValue As Type

    Property Type As Type
        Get
            If TypeValue Is Nothing Then TypeValue = GetType(String)

            Return TypeValue
        End Get
        Set(Value As Type)
            TypeValue = Value
        End Set
    End Property

    Private DescriptionValue As String

    Property Description() As String
        Get
            If DescriptionValue Is Nothing Then DescriptionValue = ""

            Return DescriptionValue
        End Get
        Set(Value As String)
            DescriptionValue = Value
        End Set
    End Property

    Shared Function GetTips() As StringPairList
        Dim ret As New StringPairList

        For Each i In GetMacros(True)
            ret.Add(i.Name, i.Description)
        Next

        Return ret
    End Function

    Shared Function GetTipsFriendly(convertHTMLChars As Boolean) As StringPairList
        Dim ret As New StringPairList

        For Each i As Macro In GetMacros()
            If convertHTMLChars Then
                ret.Add(HelpDocument.ConvertChars(i.FriendlyName), i.Description)
            Else
                ret.Add(i.FriendlyName, i.Description)
            End If
        Next

        Return ret
    End Function

    Overrides Function ToString() As String
        Return Name
    End Function

    Function CompareTo(other As Macro) As Integer Implements System.IComparable(Of Macro).CompareTo
        Return Name.CompareTo(other.Name)
    End Function

    Shared Function GetMacros(Optional includeSpecial As Boolean = False) As List(Of Macro)
        Dim ret As New List(Of Macro)

        If includeSpecial Then
            ret.Add(New Macro("eval:<expression>", "Eval Math Expression", GetType(String), "Evaluates a math expression which may contain default macros."))
            ret.Add(New Macro("filter:<name>", "Filter", GetType(String), "Returns the script code of a filter of the active project that matches the specified name."))
            ret.Add(New Macro("media_info_video:<property>", "MediaInfo Video Property", GetType(String), "Returns a MediaInfo video property for the source file."))
            ret.Add(New Macro("media_info_audio:<property>", "MediaInfo Audio Property", GetType(String), "Returns a MediaInfo audio property for the video source file."))
            ret.Add(New Macro("app:<name>", "Application File Path", GetType(String), "Returns the path of a aplication. Possible names are: " + Package.Items.Values.Select(Function(arg) arg.Name).Join(", ")))
            ret.Add(New Macro("app_dir:<name>", "Application Directory", GetType(String), "Returns the directory of a aplication. Possible names are: " + Package.Items.Values.Select(Function(arg) arg.Name).Join(", ")))
            ret.Add(New Macro("$select:<param1;param2;...>$", "Select", GetType(String), "String selected from dropdown."))
            ret.Add(New Macro("$enter_text:<prompt>$", "Enter Text (Params)", GetType(String), "Text entered in a input box."))
            ret.Add(New Macro("$browse_file$", "Browse For File", GetType(String), "Filepath returned from a file browser."))
            ret.Add(New Macro("$enter_text$", "Enter Text", GetType(String), "Text entered in a input box."))
        End If

        ret.Add(New Macro("audio_bitrate", "Audio Bitrate", GetType(Integer), "Overall audio bitrate."))
        ret.Add(New Macro("audio_file1", "First Audio File", GetType(String), "File path of the first audio file."))
        ret.Add(New Macro("audio_file2", "Second Audio File", GetType(String), "File path of the second audio file."))
        ret.Add(New Macro("compressibility", "Compressibility", GetType(Integer), "Compressibility value."))
        ret.Add(New Macro("crop_bottom", "Crop Bottom", GetType(Integer), "Bottom crop value."))
        ret.Add(New Macro("crop_height", "Crop Height", GetType(Integer), "Crop height."))
        ret.Add(New Macro("crop_left", "Crop Left", GetType(Integer), "Left crop value."))
        ret.Add(New Macro("crop_right", "Crop Right", GetType(Integer), "Right crop value."))
        ret.Add(New Macro("crop_top", "Crop Top", GetType(Integer), "Top crop value."))
        ret.Add(New Macro("crop_width", "Crop Width", GetType(Integer), "Crop width."))
        ret.Add(New Macro("delay", "Audio Delay 1", GetType(Integer), "Audio delay of the first audio track."))
        ret.Add(New Macro("delay2", "Audio Delay 2", GetType(Integer), "Audio delay of the second audio track."))
        ret.Add(New Macro("encoder_ext", "Encoder File Extension", GetType(String), "File extension of the format the encoder of the active project outputs."))
        ret.Add(New Macro("encoder_out_file", "Encoder Output File", GetType(String), "Output file of the video encoder."))
        ret.Add(New Macro("muxer_ext", "Muxer Extension", GetType(String), "Output extension of the active muxer."))
        ret.Add(New Macro("player", "Player", GetType(Integer), "Path of MPC Player."))
        ret.Add(New Macro("plugin_dir", "Plugin Directory", GetType(String), "AviSynth/VapourSynth plugin directory."))
        ret.Add(New Macro("pos_frame", "Position In Frames", GetType(Integer), "Current preview position in frames."))
        ret.Add(New Macro("pos_ms", "Position In Millisecons", GetType(Integer), "Current preview position in milliseconds."))
        ret.Add(New Macro("processing", "Processing", GetType(String), "Returns 'True' if a job is currently processing otherwise 'False'."))
        ret.Add(New Macro("programs_dir", "Programs Directory", GetType(String), "Programs system directory."))
        ret.Add(New Macro("script_dir", "Script Directory", GetType(String), "Users C# and PowerShell scripts directory."))
        ret.Add(New Macro("script_ext", "Script File Extension", GetType(String), "File extension of the AviSynth/VapourSynth script so either avs or vpy."))
        ret.Add(New Macro("script_file", "Script Path", GetType(String), "Path of the AviSynth/VapourSynth script."))
        ret.Add(New Macro("sel_end", "Selection End", GetType(Integer), "End position of the first selecion in the preview."))
        ret.Add(New Macro("sel_start", "Selection Start", GetType(Integer), "Start position of the first selecion in the preview."))
        ret.Add(New Macro("settings_dir", "Settings Directory", GetType(String), "Path of the settings direcory."))
        ret.Add(New Macro("source_dir", "Source Directory", GetType(String), "Directory of the source file."))
        ret.Add(New Macro("source_dir_name", "Source Directory Name", GetType(String), "Name of the source file directory."))
        ret.Add(New Macro("source_dir_parent", "Source Directory Parent", GetType(String), "Parent directory of the source file directory."))
        ret.Add(New Macro("source_file", "Source File Path", GetType(String), "File path of the source video."))
        ret.Add(New Macro("source_files", "Source Files Blank", GetType(String), "Source files in quotes separated by a blank."))
        ret.Add(New Macro("source_files_comma", "Source Files Comma", GetType(String), "Source files in quotes separated by comma."))
        ret.Add(New Macro("source_framerate", "Source Framerate", GetType(Integer), "Frame rate returned by the source filter AviSynth section."))
        ret.Add(New Macro("source_frames", "Source Frames", GetType(Integer), "Length in frames of the source video."))
        ret.Add(New Macro("source_height", "Source Image Height", GetType(Integer), "Image height of the source video."))
        ret.Add(New Macro("source_name", "Source Filename Without Extension", GetType(String), "The name of the source file without file extension."))
        ret.Add(New Macro("source_seconds", "Source Seconds", GetType(Integer), "Length in seconds of the source video."))
        ret.Add(New Macro("source_width", "Source Image Width", GetType(Integer), "Image width of the source video."))
        ret.Add(New Macro("startup_dir", "Startup Directory", GetType(String), "Directory of the application."))
        ret.Add(New Macro("system_dir", "System Directory", GetType(String), "System directory."))
        ret.Add(New Macro("target_dir", "Target Directory", GetType(String), "Directory of the target file."))
        ret.Add(New Macro("target_file", "Target File Path", GetType(String), "File path of the target file."))
        ret.Add(New Macro("target_framerate", "Target Framerate", GetType(Integer), "Frame rate of the target video."))
        ret.Add(New Macro("target_frames", "Target Frames", GetType(Integer), "Length in frames of the target video."))
        ret.Add(New Macro("target_height", "Target Image Height", GetType(Integer), "Image height of the target video."))
        ret.Add(New Macro("target_name", "Target Filename Without Extension", GetType(String), "Name of the target file without file extension."))
        ret.Add(New Macro("target_sar", "Target Sample Aspect Ratio", GetType(String), "Target sample aspect ratio (also known as PAR (pixel aspect ratio))."))
        ret.Add(New Macro("target_seconds", "Target Seconds", GetType(Integer), "Length in seconds of the target video."))
        ret.Add(New Macro("target_size", "Target Size", GetType(Integer), "Size of the target video in kilo bytes."))
        ret.Add(New Macro("target_width", "Target Image Width", GetType(Integer), "Image width of the target video."))
        ret.Add(New Macro("temp_file", "Temp File", GetType(String), "File located in the temp directory using the same name as the source file."))
        ret.Add(New Macro("template_name", "Template Name", GetType(String), "Name of the template the active project is based on."))
        ret.Add(New Macro("text_editor", "Text Editor", GetType(String), "Path of the application currently associated with TXT files."))
        ret.Add(New Macro("version", "Version", GetType(String), "StaxRip version."))
        ret.Add(New Macro("video_bitrate", "Video Bitrate", GetType(Integer), "Video bitrate"))
        ret.Add(New Macro("working_dir", "Working Directory", GetType(String), "Directory of the source file or the temp directory if enabled."))

        ret.Sort()

        Return ret
    End Function

    Shared Function Solve(value As String) As String
        Return Solve(value, False)
    End Function

    Shared Function Solve(value As String, silent As Boolean) As String
        If value = "" Then Return ""

        If Not silent AndAlso value.Contains("$") Then
            If value.Contains("$browse_file$") Then
                Using d As New OpenFileDialog
                    If d.ShowDialog = DialogResult.OK Then
                        value = value.Replace("$browse_file$", d.FileName)
                    End If
                End Using

                Return value
            End If

            If value.Contains("$enter_text$") Then
                Dim v = InputBox.Show("Please enter some text.")

                If v <> "" Then value = value.Replace("$enter_text$", v)

                Return value
            End If

            If value.Contains("$enter_text:") Then
                Dim mc = Regex.Matches(value, "\$enter_text:(.+?)\$")

                For Each i As Match In mc
                    Dim v = InputBox.Show(i.Groups(1).Value)

                    If v <> "" Then value = value.Replace(i.Value, v)
                Next

                Return value
            End If

            If value.Contains("$select:") Then
                Dim mc = Regex.Matches(value, "\$select:(.+?)\$")

                For Each i As Match In mc
                    Dim items = i.Groups(1).Value.SplitNoEmpty(";")

                    If items.Length > 0 Then
                        Dim f As New SelectionBox(Of String)
                        f.Text = "Please select a item."
                        f.Title = "Select"

                        For Each i2 As String In items
                            f.AddItem(i2)
                        Next

                        If f.Show = DialogResult.OK Then
                            value = value.Replace(i.Value, f.SelectedItem)
                        End If
                    End If
                Next

                Return value
            End If
        End If

        If Not value.Contains("%") Then Return value

        If value.Contains("%source_file%") Then value = value.Replace("%source_file%", p.SourceFile)
        If Not value.Contains("%") Then Return value

        If value.Contains("%working_dir%") Then value = value.Replace("%working_dir%", p.TempDir)
        If Not value.Contains("%") Then Return value

        If value.Contains("%temp_file%") Then value = value.Replace("%temp_file%", p.TempDir + Filepath.GetBase(p.SourceFile))
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_name%") Then value = value.Replace("%source_name%", Filepath.GetBase(p.SourceFile))
        If Not value.Contains("%") Then Return value

        If value.Contains("%version%") Then value = value.Replace("%version%", Application.ProductVersion)
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_width%") Then value = value.Replace("%source_width%", p.SourceWidth.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_height%") Then value = value.Replace("%source_height%", p.SourceHeight.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_seconds%") Then value = value.Replace("%source_seconds%", p.SourceSeconds.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_frames%") Then value = value.Replace("%source_frames%", p.SourceFrames.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_framerate%") Then value = value.Replace("%source_framerate%", p.SourceFrameRate.ToString("f6", CultureInfo.InvariantCulture))
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_dir%") Then value = value.Replace("%source_dir%", Filepath.GetDir(p.SourceFile))
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_dir_parent%") Then value = value.Replace("%source_dir_parent%", DirPath.GetParent(Filepath.GetDir(p.SourceFile)))
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_dir_name%") Then value = value.Replace("%source_dir_name%", DirPath.GetName(Filepath.GetDir(p.SourceFile)))
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_width%") Then value = value.Replace("%target_width%", p.TargetWidth.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_height%") Then value = value.Replace("%target_height%", p.TargetHeight.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_seconds%") Then value = value.Replace("%target_seconds%", p.TargetSeconds.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_frames%") Then value = value.Replace("%target_frames%", p.Script.GetFrames.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_framerate%") Then value = value.Replace("%target_framerate%", p.Script.GetFramerate.ToString("f6", CultureInfo.InvariantCulture))
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_size%") Then value = value.Replace("%target_size%", (p.TargetSize * 1024).ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_file%") Then value = value.Replace("%target_file%", p.TargetFile)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_dir%") Then value = value.Replace("%target_dir%", Filepath.GetDir(p.TargetFile))
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_name%") Then value = value.Replace("%target_name%", p.Name)
        If Not value.Contains("%") Then Return value

        If value.Contains("%target_sar%") Then
            Dim par = Calc.GetTargetPAR
            value = value.Replace("%target_sar%", par.X & ":" & par.Y)
        End If

        If Not value.Contains("%") Then Return value

        If value.Contains("%crop_width%") Then value = value.Replace("%crop_width%", (p.SourceWidth - p.CropLeft - p.CropRight).ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%crop_height%") Then value = value.Replace("%crop_height%", (p.SourceHeight - p.CropTop - p.CropBottom).ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%crop_left%") Then value = value.Replace("%crop_left%", p.CropLeft.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%crop_top%") Then value = value.Replace("%crop_top%", p.CropTop.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%crop_right%") Then value = value.Replace("%crop_right%", p.CropRight.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%crop_bottom%") Then value = value.Replace("%crop_bottom%", p.CropBottom.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%video_bitrate%") Then value = value.Replace("%video_bitrate%", p.VideoBitrate.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%audio_bitrate%") Then value = value.Replace("%audio_bitrate%", (p.Audio0.Bitrate + p.Audio1.Bitrate).ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%audio_file1%") Then value = value.Replace("%audio_file1%", p.Audio0.File)
        If Not value.Contains("%") Then Return value

        If value.Contains("%audio_file2%") Then value = value.Replace("%audio_file2%", p.Audio1.File)
        If Not value.Contains("%") Then Return value

        If value.Contains("%delay%") Then value = value.Replace("%delay%", p.Audio0.Delay.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%delay2%") Then value = value.Replace("%delay2%", p.Audio1.Delay.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%startup_dir%") Then value = value.Replace("%startup_dir%", Folder.Startup)
        If Not value.Contains("%") Then Return value

        If value.Contains("%system_dir%") Then value = value.Replace("%system_dir%", Folder.System)
        If Not value.Contains("%") Then Return value

        If value.Contains("%script_dir%") Then value = value.Replace("%script_dir%", Folder.Script)
        If Not value.Contains("%") Then Return value

        If value.Contains("%programs_dir%") Then value = value.Replace("%programs_dir%", Folder.Programs)
        If Not value.Contains("%") Then Return value

        If value.Contains("%plugin_dir%") Then value = value.Replace("%plugin_dir%", Folder.Plugins)
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_files_comma%") Then value = value.Replace("%source_files_comma%", """" + String.Join(""",""", p.SourceFiles.ToArray) + """")
        If Not value.Contains("%") Then Return value

        If value.Contains("%source_files%") Then value = value.Replace("%source_files%", """" + String.Join(""" """, p.SourceFiles.ToArray) + """")
        If Not value.Contains("%") Then Return value

        If value.Contains("%compressibility%") Then value = value.Replace("%compressibility%", Math.Round(p.Compressibility, 3).ToString.Replace(",", "."))
        If Not value.Contains("%") Then Return value

        If value.Contains("\\") Then value = value.Replace("\\", "\")
        If Not value.Contains("%") Then Return value

        If value.Contains("%encoder_out_file%") Then value = value.Replace("%encoder_out_file%", p.VideoEncoder.OutputPath)
        If Not value.Contains("%") Then Return value

        If value.Contains("%encoder_ext%") Then value = value.Replace("%encoder_ext%", p.VideoEncoder.OutputExt)
        If Not value.Contains("%") Then Return value

        If value.Contains("%muxer_ext%") Then value = value.Replace("%muxer_ext%", p.VideoEncoder.Muxer.OutputExt)
        If Not value.Contains("%") Then Return value

        If value.Contains("%script_ext%") Then value = value.Replace("%script_ext%", p.Script.FileType)
        If Not value.Contains("%") Then Return value

        If value.Contains("%pos_frame%") Then value = value.Replace("%pos_frame%", s.LastPosition.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%template_name%") Then value = value.Replace("%template_name%", p.TemplateName)
        If Not value.Contains("%") Then Return value

        If value.Contains("%settings_dir%") Then value = value.Replace("%settings_dir%", Folder.Settings)
        If Not value.Contains("%") Then Return value

        If value.Contains("%player%") Then value = value.Replace("%player%", Package.MPC.Path)
        If Not value.Contains("%") Then Return value

        If value.Contains("%text_editor%") Then value = value.Replace("%text_editor%", g.GetTextEditor)
        If Not value.Contains("%") Then Return value

        If value.Contains("%processing%") Then value = value.Replace("%processing%", g.IsProcessing.ToString)
        If Not value.Contains("%") Then Return value

        If value.Contains("%script_file%") Then
            p.Script.Synchronize()
            value = value.Replace("%script_file%", p.Script.Path)
        End If

        If p.Ranges.Count > 0 Then
            If value.Contains("%sel_start%") Then value = value.Replace("%sel_start%", p.Ranges(0).Start.ToString)
            If Not value.Contains("%") Then Return value

            If value.Contains("%sel_end%") Then value = value.Replace("%sel_end%", p.Ranges(0).End.ToString)
            If Not value.Contains("%") Then Return value
        Else
            If value.Contains("%sel_start%") Then value = value.Replace("%sel_start%", 0.ToString)
            If Not value.Contains("%") Then Return value

            If value.Contains("%sel_end%") Then value = value.Replace("%sel_end%", 0.ToString)
            If Not value.Contains("%") Then Return value
        End If

        If value.Contains("%pos_ms%") Then value = value.Replace("%pos_ms%", g.GetPreviewPosMS.ToString)

        If Not value.Contains("%") Then Return value

        If value.Contains("%app:") Then
            Dim mc = Regex.Matches(value, "%app:(.+?)%")

            For Each i As Match In mc
                Dim package = StaxRip.Package.Items.Values.FirstOrDefault(Function(a) a.Name = i.Groups(1).Value)

                If package?.VerifyOK Then
                    Dim path = package.Path

                    If path <> "" Then
                        value = value.Replace(i.Value, path)
                        If Not value.Contains("%") Then Return value
                    End If
                End If
            Next
        End If

        If value.Contains("%media_info_video:") Then
            For Each i As Match In Regex.Matches(value, "%media_info_video:(.+?)%")
                value = value.Replace(i.Value, MediaInfo.GetVideo(p.LastOriginalSourceFile, i.Groups(1).Value))
            Next
        End If

        If value.Contains("%media_info_audio:") Then
            For Each i As Match In Regex.Matches(value, "%media_info_audio:(.+?)%")
                value = value.Replace(i.Value, MediaInfo.GetAudio(p.LastOriginalSourceFile, i.Groups(1).Value))
            Next
        End If

        If Not value.Contains("%") Then Return value

        If value.Contains("%app_dir:") Then
            For Each i As Match In Regex.Matches(value, "%app_dir:(.+?)%")
                Dim package = StaxRip.Package.Items.Values.FirstOrDefault(Function(a) a.Name = i.Groups(1).Value)

                If package?.VerifyOK Then
                    Dim path = package.Path

                    If path <> "" Then
                        value = value.Replace(i.Value, Filepath.GetDir(path))
                        If Not value.Contains("%") Then Return value
                    End If
                End If
            Next
        End If

        If Not value.Contains("%") Then Return value

        If value.Contains("%filter:") Then
            Dim mc = Regex.Matches(value, "%filter:(.+?)%")

            For Each i As Match In mc
                For Each i2 In p.Script.Filters
                    If i2.Active AndAlso i2.Path.ToUpper = i.Groups(1).Value.ToUpper Then
                        value = value.Replace(i.Value, i2.Script)
                        If Not value.Contains("%") Then Return value
                        Exit For
                    End If
                Next

                value = value.Replace(i.Value, "")
            Next
        End If

        If Not value.Contains("%") Then Return value

        If value.Contains("%eval:") Then
            If Not value.Contains("%eval:<expression>%") Then
                Dim mc = Regex.Matches(value, "%eval:(.+?)%")

                For Each i As Match In mc
                    Try
                        value = value.Replace(i.Value, Misc.Eval(i.Groups(1).Value).ToString)

                        If Not value.Contains("%") Then
                            Return value
                        End If
                    Catch ex As Exception
                        MsgWarn("Failed to solve macro '" + i.Value + "': " + ex.Message)
                    End Try
                Next
            End If
        End If

        Return value
    End Function
End Class

<Serializable()>
Public Class ObjectStorage
    Private StringDictionary As New Dictionary(Of String, String)
    Private IntDictionary As New Dictionary(Of String, Integer)

    Private BoolDictionaryValue As Dictionary(Of String, Boolean)

    ReadOnly Property BoolDictionary() As Dictionary(Of String, Boolean)
        Get
            If BoolDictionaryValue Is Nothing Then
                BoolDictionaryValue = New Dictionary(Of String, Boolean)
            End If

            Return BoolDictionaryValue
        End Get
    End Property

    Function GetBool(key As String) As Boolean
        Return GetBool(key, False)
    End Function

    Function GetBool(key As String, defaultValue As Boolean) As Boolean
        If BoolDictionary.ContainsKey(key) Then
            Return BoolDictionary(key)
        End If

        Return defaultValue
    End Function

    Sub SetBool(key As String, Value As Boolean)
        BoolDictionary(key) = Value
    End Sub

    Function GetInt(key As String) As Integer
        Return GetInt(key, 0)
    End Function

    Function GetInt(key As String, defaultValue As Integer) As Integer
        If IntDictionary.ContainsKey(key) Then
            Return IntDictionary(key)
        End If

        Return defaultValue
    End Function

    Sub SetInt(key As String, value As Integer)
        IntDictionary(key) = value
    End Sub

    Function GetString(key As String) As String
        Return GetString(key, Nothing)
    End Function

    Function GetString(key As String, defaultValue As String) As String
        If StringDictionary.ContainsKey(key) Then
            Return StringDictionary(key)
        End If

        Return defaultValue
    End Function

    Sub SetString(key As String, value As String)
        StringDictionary(key) = value
    End Sub
End Class

Public Enum CompCheckAction
    [Nothing]
    <DispName("image size")> AdjustImageSize
    <DispName("file size")> AdjustFileSize
End Enum

<Serializable()>
Public Class EventCommand
    Property Name As String = "???"
    Property Enabled As Boolean = True
    Property CriteriaList As New List(Of Criteria)
    Property OrOnly As Boolean
    Property CommandParameters As CommandParameters
    Property [Event] As ApplicationEvent

    Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Enum ApplicationEvent
    <DispName("After Project Loaded")> ProjectLoaded
    <DispName("After Project Encoded")> JobEncoded
    <DispName("Before Project Encoding")> BeforeEncoding
    <DispName("After Source Loaded")> AfterSourceLoaded
    <DispName("Application Exit")> ApplicationExit
    <DispName("After Project Or Source Loaded")> ProjectOrSourceLoaded
    <DispName("After Jobs Encoded")> JobsEncoded
End Enum

Public Enum DynamicMenuItemID
    Audio1Profiles
    Audio2Profiles
    EncoderProfiles
    FilterSetupProfiles
    MuxerProfiles
    RecentProjects
    TemplateProjects
    HelpApplications
    Scripts
End Enum

Public Enum SourceInputMode
    Combine
    FileBatch
    DirectoryBatch
End Enum

Class Startup
    <STAThread()>
    Shared Sub Main()
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf g.OnUnhandledException
        SetProcessDPIAware()
        Application.EnableVisualStyles()
        'use new GDI/TextRenderer by default instead of old GDI+/Graphics.DrawString
        Application.SetCompatibleTextRenderingDefault(False)
        Dim args = Environment.GetCommandLineArgs.Skip(1)

        If args.Count = 2 AndAlso args(0) = "-mediainfo" Then
            ToolStripManager.Renderer = New ToolStripRendererEx(ToolStripRenderMode.SystemDefault)
            Application.Run(New MediaInfoForm(args(1)) With {.ShowInTaskbar = True})
        Else
            Application.Run(New MainForm())
        End If
    End Sub

    <DllImport("user32.dll")>
    Shared Function SetProcessDPIAware() As Boolean
    End Function
End Class

<Serializable()>
Class Dummy
End Class

Class KeyValueList(Of T1, T2)
    Inherits List(Of KeyValuePair(Of T1, T2))

    Overloads Sub Add(key As T1, value As T2)
        Add(New KeyValuePair(Of T1, T2)(key, value))
    End Sub
End Class

Class GUIDS
    Shared Property LAVSplitter As String = "{171252A0-8820-4AFE-9DF8-5C92B2D66B04}"
    Shared Property LAVVideoDecoder As String = "{EE30215D-164F-4A92-A4EB-9D4C13390F9F}"
    Shared Property HaaliMuxer As String = "{A28F324B-DDC5-4999-AA25-D3A7E25EF7A8}"
End Class

Class M2TSStream
    Property Text As String = "Nothing"
    Property Codec As String = ""
    Property OutputType As String = ""
    Property Options As String = ""
    Property ID As Integer
    Property IsVideo As Boolean
    Property IsAudio As Boolean
    Property IsSubtitle As Boolean
    Property IsChapters As Boolean
    Property Language As New Language
    Property Checked As Boolean
    Property ListViewItem As ListViewItem

    Sub UpdateListViewItem()
        ListViewItem.Text = ToString()
    End Sub

    Public Overrides Function ToString() As String
        Dim ret = Text

        If ret.Contains("TrueHD/AC3") Then ret = ret.Replace("TrueHD/AC3", "THD+AC3")
        If ret.Contains("DTS Master Audio") Then ret = ret.Replace("DTS Master Audio", "DTS-MA")
        If ret.Contains("DTS Hi-Res") Then ret = ret.Replace("DTS Hi-Res", "DTS-HRA")
        If ret.Contains("DTS Express") Then ret = ret.Replace("DTS Express", "DTS-EX")

        If IsAudio Then
            ret += "  ->  " + OutputType

            If Options <> "" Then
                ret += ": " + Options
            End If
        End If

        Return ret
    End Function
End Class

<Serializable>
Public Class AudioStream
    Property BitDepth As Integer
    Property Bitrate As Integer
    Property BitrateCore As Integer
    Property Channels As Integer
    Property ChannelsCore As Integer
    Property Codec As String
    Property CodecString As String
    Property Delay As Integer
    Property Format As String
    Property FormatProfile As String 'was only field to show DTS MA
    Property ID As Integer
    Property Language As Language
    Property SamplingRate As Integer
    Property StreamOrder As Integer
    Property Title As String
    Property Enabled As Boolean = True

    ReadOnly Property Name As String
        Get
            Dim sb As New StringBuilder()
            sb.Append("ID" & (StreamOrder + 1))

            If CodecString <> "" Then
                Select Case CodecString
                    Case "MPEG-1 Audio layer 2"
                        sb.Append(" MP2")
                    Case "MPEG-1 Audio layer 3"
                        sb.Append(" MP3")
                    Case "TrueHD / AC3"
                        sb.Append(" THD+AC3")
                    Case "AC3+"
                        sb.Append(" E-AC3")
                    Case Else
                        Select Case Codec
                            Case "Atmos / TrueHD"
                                sb.Append(" THD Atmos")
                            Case "TrueHD / AC3"
                                sb.Append(" THD+AC3")
                            Case Else
                                Select Case FormatProfile
                                    Case "MA / Core"
                                        sb.Append(" DTS MA/Core")
                                    Case "HRA / Core"
                                        sb.Append(" DTS HRA/Core")
                                    Case Else
                                        sb.Append(" " + CodecString)
                                End Select
                        End Select
                End Select
            End If

            If ChannelsCore > 0 Then
                sb.Append(" " & Channels & "/" & ChannelsCore & "ch")
            ElseIf Channels > 0 Then
                sb.Append(" " & Channels & "ch")
            End If

            If BitDepth > 0 Then sb.Append(" " & BitDepth & "Bit")
            If SamplingRate > 0 Then sb.Append(" " & SamplingRate & "Hz")

            If BitrateCore > 0 Then
                sb.Append(" " & If(Bitrate = 0, "?", Bitrate.ToString) & "/" & BitrateCore & "Kbps")
            ElseIf Bitrate > 0 Then
                sb.Append(" " & Bitrate & "Kbps")
            End If

            If Delay <> 0 Then sb.Append(" " & Delay & "ms")
            If Language.TwoLetterCode <> "iv" Then sb.Append(" " + Language.Name)
            If Title <> "" AndAlso Title <> " " Then sb.Append(" " + Title)

            Return sb.ToString
        End Get
    End Property

    ReadOnly Property Extension() As String
        Get
            Select Case CodecString
                Case "AAC LC", "AAC LC-SBR", "AAC LC-SBR-PS"
                    Return ".m4a"
                Case "AC3"
                    Return ".ac3"
                Case "DTS"
                    Return ".dts"
                Case "DTS-HD"
                    If FormatProfile = "MA / Core" Then
                        Return ".dtsma"
                    ElseIf FormatProfile = "HRA / Core" Then
                        Return ".dtshr"
                    Else
                        Return ".dtshd"
                    End If
                Case "PCM", "ADPCM"
                    Return ".wav"
                Case "MPEG-1 Audio layer 2"
                    Return ".mp2"
                Case "MPEG-1 Audio layer 3"
                    Return ".mp3"
                Case "TrueHD / AC3"
                    Return ".thd"
                Case "Flac"
                    Return ".flac"
                Case "Vorbis"
                    Return ".ogg"
                Case "Opus"
                    Return ".opus"
                Case "TrueHD", "Atmos / TrueHD"
                    Return ".thd"
                Case "AC3+"
                    Return ".eac3"
                Case Else
                    Return ".mka"
            End Select
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Class VideoStream
    Property Format As String
    Property StreamOrder As Integer

    ReadOnly Property Extension() As String
        Get
            Select Case Format
                Case "MPEG Video"
                    Return ".mpg"
                Case "AVC"
                    Return ".h264"
                Case "MPEG-4 Visual", "JPEG"
                    Return ".avi"
                Case "HEVC"
                    Return "h265"
            End Select
        End Get
    End Property
End Class

Class Video
    Shared Sub DemuxMKV(sourcefile As String)
        Dim streams = MediaInfo.GetVideoStreams(sourcefile)
        If streams.Count = 0 Then Exit Sub
        Dim stream = streams(0)
        If stream.Extension = "" Then Throw New Exception("demuxing of video stream format is not implemented")
        Dim outPath = p.TempDir + Filepath.GetBase(sourcefile) + "_out" + stream.Extension

        Using proc As New Proc
            proc.Init("Demux video using mkvextract " + Package.mkvextract.Version, "Progress: ")
            proc.Encoding = Encoding.UTF8
            proc.File = Package.mkvextract.Path
            proc.Arguments = "tracks """ + sourcefile + """ " & stream.StreamOrder &
                ":""" + outPath + """ --ui-language en"
            proc.AllowedExitCodes = {0, 1, 2}
            proc.Start()
        End Using

        If File.Exists(outPath) Then
            Log.WriteLine(MediaInfo.GetSummary(outPath))
        Else
            Log.Write("Error", "no output found")
        End If
    End Sub
End Class

<Serializable()>
Public Class Subtitle
    Property Title As String = ""
    Property Path As String
    Property CodecString As String
    Property Format As String
    Property ID As Integer
    Property StreamOrder As Integer
    Property IndexIDX As Integer
    Property Language As Language
    Property [Default] As Boolean
    Property Forced As Boolean
    Property Enabled As Boolean = True
    Property Size As Long

    Sub New()
        Language = New Language
    End Sub

    Sub New(lang As Language)
        Language = lang
    End Sub

    ReadOnly Property Filename As String
        Get
            Dim ret = "ID" & (StreamOrder + 1)
            ret += " " + Language.Name

            If Title <> "" AndAlso Title <> " " AndAlso Not Title.ContainsUnicode AndAlso
                p.SourceFile <> "" AndAlso p.SourceFile.Length < 130 Then

                ret += " " + Title.Shorten(30)
            End If

            If Not Filepath.IsValidFileSystemName(ret) Then
                ret = Filepath.RemoveIllegalCharsFromName(ret)
            End If

            Return ret
        End Get
    End Property

    ReadOnly Property Extension As String
        Get
            Select Case CodecString
                Case "VobSub"
                    Return ".idx"
                Case "S_HDMV/PGS", "PGS"
                    Return ".sup"
                Case "S_TEXT/ASS", "ASS"
                    Return ".ass"
                Case "S_TEXT/UTF8", "UTF-8"
                    Return ".srt"
                Case "S_TEXT/SSA", "SSA"
                    Return ".ssa"
                Case "S_TEXT/USF", "USF"
                    Return ".usf"
                Case "Timed"
                    Return ".srt"
            End Select
        End Get
    End Property

    ReadOnly Property TypeName As String
        Get
            Dim ret = Extension
            If ret = "" Then ret = Path.ExtFull
            Return ret.TrimStart("."c).ToUpper.Replace("SUP", "PGS").Replace("IDX", "VobSub")
        End Get
    End Property

    Shared Function Create(path As String) As List(Of Subtitle)
        Dim ret As New List(Of Subtitle)
        If New FileInfo(path).Length = 0 Then Return ret

        If path.Ext = "idx" Then
            Dim indexData As Integer
            Dim st As Subtitle = Nothing

            For Each i In File.ReadAllText(path).SplitLinesNoEmpty
                If i.StartsWith("id: ") AndAlso i Like "id: ??, index: *" Then
                    st = New Subtitle

                    If path.Contains("forced") Then st.Forced = True

                    Try
                        st.Language = New Language(New CultureInfo(i.Substring(4, 2)))
                    Catch
                        st.Language = New Language(CultureInfo.InvariantCulture)
                    End Try

                    Dim autoCode = p.PreferredSubtitles.ToLower.SplitNoEmptyAndWhiteSpace(",", ";", " ")
                    st.Enabled = autoCode.ContainsAny("all", st.Language.TwoLetterCode, st.Language.ThreeLetterCode)

                    If Not st Is Nothing Then
                        st.IndexIDX = CInt(Regex.Match(i, ", index: (\d+)").Groups(1).Value)
                    End If
                End If

                If Not st Is Nothing AndAlso i.StartsWith("timestamp: ") Then
                    st.StreamOrder = indexData
                    st.Path = path
                    indexData += 1
                    st.Size = CInt(New FileInfo(path).Length / 1024)
                    Dim subFile = path.ChangeExt("sub")
                    If File.Exists(subFile) Then st.Size += New FileInfo(subFile).Length
                    ret.Add(st)
                    st = Nothing
                End If
            Next
        ElseIf path.Ext.EqualsAny("mkv", "mp4", "m2ts") Then
            For Each i In MediaInfo.GetSubtitles(path)
                If i.Size = 0 Then
                    Select Case i.TypeName
                        Case "SRT"
                            i.Size = 10L * p.TargetSeconds
                        Case "VobSub"
                            i.Size = 1250L * p.TargetSeconds
                        Case "PGS"
                            i.Size = 5000L * p.TargetSeconds
                    End Select
                End If

                i.Path = path
                ret.Add(i)
            Next
        Else
            Dim st As New Subtitle()
            st.Size = New FileInfo(path).Length
            Dim match = Regex.Match(path, " ID(\d+)")
            If match.Success Then st.StreamOrder = match.Groups(1).Value.ToInt - 1

            For Each i In Language.Languages
                If path.Contains(i.CultureInfo.EnglishName) Then
                    st.Language = i
                    Exit For
                End If
            Next

            Dim autoCode = p.PreferredSubtitles.ToLower.SplitNoEmptyAndWhiteSpace(",", ";", " ")
            st.Enabled = autoCode.ContainsAny("all", st.Language.TwoLetterCode, st.Language.ThreeLetterCode)

            st.Path = path
            ret.Add(st)
        End If

        Return ret
    End Function
End Class

<Serializable>
Public Class PrimitiveStore
    Property Bool As New Dictionary(Of String, Boolean)
    Property Int As New Dictionary(Of String, Integer)
    Property Sng As New Dictionary(Of String, Single)
    Property [String] As New Dictionary(Of String, String)
End Class

Public Enum ContainerStreamType
    Unknown
    Audio
    Video
    Subtitle
    Attachment
    Chapters
End Enum

Class FileTypes
    Shared Property Audio As String() = {"aac", "ac3", "dts", "dtsma", "dtshr", "dtshd", "eac3", "flac", "m4a", "mka", "mp2", "mp3", "mpa", "ogg", "opus", "thd", "thd+ac3", "true-hd", "truehd", "wav"}
    Shared Property VideoAudio As String() = {"avi", "mp4", "mkv", "divx", "flv", "mov", "mpeg", "mpg", "ts", "m2ts", "vob", "webm", "wmv", "pva", "ogg", "ogm"}
    Shared Property BeSweetInput As String() = {"wav", "mp2", "mpa", "mp3", "ac3", "ogg"}
    Shared Property DGDecNVInput As String() = {"264", "h264", "avc", "mkv", "mp4", "mpg", "vob", "ts", "m2ts", "mts", "m2t", "mpv", "m2v"}
    Shared Property eac3toInput As String() = {"dts", "dtshd", "dtshr", "dtsma", "evo", "mkv", "vob", "ts", "m2ts", "wav", "w64", "pcm", "raw", "flac", "ac3", "eac3", "thd", "thd+ac3", "mlp", "mp2", "mp3", "mpa"}
    Shared Property NicAudioInput As String() = {"wav", "mp2", "mpa", "mp3", "ac3", "dts"}
    Shared Property qaacInput As String() = {"wav", "flac"}
    Shared Property SubtitleExludingContainers As String() = {"ass", "idx", "smi", "srt", "ssa", "sup", "ttxt"}
    Shared Property SubtitleIncludingContainers As String() = {"m2ts", "mkv", "mp4", "ass", "idx", "smi", "srt", "ssa", "sup", "ttxt"}
    Shared Property TextSub As String() = {"ass", "idx", "smi", "srt", "ssa", "ttxt", "usf", "ssf", "psb", "sub"}
    Shared Property Video As String() = {"264", "avc", "avi", "avs", "d2v", "dgi", "dgim", "divx", "flv", "h264", "m2t", "mts", "m2ts", "m2v", "mkv", "mov", "mp4", "mpeg", "mpg", "mpv", "ogg", "ogm", "pva", "rmvb", "ts", "vob", "webm", "wmv", "y4m"}
    Shared Property VideoNoText As String() = {"264", "avc", "avi", "divx", "flv", "h264", "m2t", "mts", "m2ts", "m2v", "mkv", "mov", "mp4", "mpeg", "mpg", "mpv", "ogg", "ogm", "pva", "rmvb", "ts", "vob", "webm", "wmv", "y4m"}
    Shared Property VideoIndex As String() = {"d2v", "dgi", "dga", "dgim"}
    Shared Property VideoOnly As String() = {"m4v", "m2v", "y4m", "mpv", "avc", "hevc", "264", "h264", "265", "h265"}
    Shared Property VideoRaw As String() = {"h264", "h265", "264", "265", "avc", "hevc"}
    Shared Property VideoText As String() = {"d2v", "dgi", "dga", "dgim", "avs", "vpy"}
    Shared Property VirtualDubModInput As String() = {"ac3", "mp3", "mp2", "mpa", "wav"}

    Shared Property mkvmergeInput As String() = {"avi", "wav",
                                                 "mp4", "m4a", "aac",
                                                 "flv", "mov",
                                                 "264", "h264", "avc",
                                                 "265", "h265", "hevc",
                                                 "ac3", "eac3", "thd+ac3", "thd",
                                                 "mkv", "mka", "webm",
                                                 "mp2", "mpa", "mp3",
                                                 "ogg", "ogm",
                                                 "dts", "dtsma", "dtshr", "dtshd",
                                                 "mpg", "m2v", "mpv",
                                                 "ts", "m2ts",
                                                 "opus", "flac"}

End Class

<Serializable>
Class StringBooleanPair
    Property Key As String
    Property Value As Boolean

    Sub New(key As String, value As Boolean)
        Me.Key = key
        Me.Value = value
    End Sub

    Public Overrides Function ToString() As String
        Return Key
    End Function
End Class

Class OSVersion
    Shared Property Windows7 As Single = 6.1
    Shared Property Windows8 As Single = 6.2
    Shared Property Windows10 As Single = 10.0

    Shared ReadOnly Property Current As Single
        Get
            Return CSng(Environment.OSVersion.Version.Major +
                Environment.OSVersion.Version.Minor / 10)
        End Get
    End Property
End Class

<Serializable>
Public Class eac3toProfile
    Property Match As String
    Property Output As String
    Property Options As String

    Sub New()
    End Sub

    Sub New(match As String,
            output As String,
            options As String)

        Me.Match = match
        Me.Output = output
        Me.Options = options
    End Sub
End Class

Class BitmapUtil
    Property Data As Byte()
    Property BitmapData As BitmapData

    Shared Function Create(bmp As Bitmap) As BitmapUtil
        Dim ret As New BitmapUtil
        Dim rect As New Rectangle(0, 0, bmp.Width, bmp.Height)
        ret.BitmapData = bmp.LockBits(rect, Imaging.ImageLockMode.ReadWrite, bmp.PixelFormat)
        ' address of first line so different value for backward and forward layout
        Dim ptr = ret.BitmapData.Scan0
        Dim bytesCount = Math.Abs(ret.BitmapData.Stride) * bmp.Height
        ret.Data = New Byte(bytesCount - 1) {}
        ' works only with forward layout, easy way to get forward layout is Bitmap.Clone
        Marshal.Copy(ptr, ret.Data, 0, bytesCount)
        bmp.UnlockBits(ret.BitmapData)
        Return ret
    End Function

    Function GetPixel(x As Integer, y As Integer) As Color
        Dim pos = y * BitmapData.Stride + x * 4
        Return Color.FromArgb(Data(pos), Data(pos + 1), Data(pos + 2))
    End Function

    Function GetMax(x As Integer, y As Integer) As Integer
        Dim c = GetPixel(x, y)
        Dim ret = Math.Max(c.R, c.G)
        Return Math.Max(ret, c.B)
    End Function
End Class

Class AutoCrop
    Public Top As Integer()
    Public Bottom As Integer()
    Public Left As Integer()
    Public Right As Integer()

    Shared Function Start(bmp As Bitmap, position As Integer) As AutoCrop
        Dim ret As New AutoCrop
        Dim u = BitmapUtil.Create(bmp)
        Dim max = 20
        Dim xCount = 20
        Dim yCount = 20

        Dim xValues(xCount) As Integer

        For x = 0 To xCount
            xValues(x) = CInt(bmp.Width / (xCount + 1) * x)
        Next

        ret.Top = New Integer(xValues.Length - 1) {}
        ret.Bottom = New Integer(xValues.Length - 1) {}

        For xValue = 0 To xValues.Length - 1
            For y = 0 To u.BitmapData.Height \ 4
                If u.GetMax(xValues(xValue), y) < max Then
                    ret.Top(xValue) = y + 1
                Else
                    Exit For
                End If
            Next

            For y = u.BitmapData.Height - 1 To u.BitmapData.Height - u.BitmapData.Height \ 4 Step -1
                If u.GetMax(xValues(xValue), y) < max Then
                    ret.Bottom(xValue) = u.BitmapData.Height - y
                Else
                    Exit For
                End If
            Next
        Next

        Dim yValues(yCount) As Integer

        For x = 0 To yCount
            yValues(x) = CInt(bmp.Height / (yCount + 1) * x)
        Next

        ret.Left = New Integer(yValues.Length - 1) {}
        ret.Right = New Integer(yValues.Length - 1) {}

        For yValue = 0 To yValues.Length - 1
            For x = 0 To u.BitmapData.Width \ 4
                If x = 0 Then
                    Debug.WriteLine("x: " & x & " y: " & yValues(yValue) & " max: " & u.GetMax(x, yValues(yValue)))
                End If

                If u.GetMax(x, yValues(yValue)) < max Then
                    ret.Left(yValue) = x + 1
                Else
                    Exit For
                End If
            Next

            For x = u.BitmapData.Width - 1 To u.BitmapData.Width - u.BitmapData.Width \ 4 Step -1
                If u.GetMax(x, yValues(yValue)) < max Then
                    ret.Right(yValue) = u.BitmapData.Width - x
                Else
                    Exit For
                End If
            Next
        Next

        Return ret
    End Function
End Class

Public Enum MsgIcon
    None = MessageBoxIcon.None
    Info = MessageBoxIcon.Information
    [Error] = MessageBoxIcon.Error
    Warning = MessageBoxIcon.Warning
    Question = MessageBoxIcon.Question
End Enum

Public Enum DemuxMode
    <DispName("Show Dialog")> Dialog
    Preferred
    All
    None
End Enum

Public Class StringLogicalComparer
    Implements IComparer, IComparer(Of String)

    <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function StrCmpLogical(x As String, y As String) As Integer
    End Function

    Private Function IComparer_Compare(x As Object, y As Object) As Integer Implements IComparer.Compare
        Return StrCmpLogical(x.ToString(), y.ToString())
    End Function

    Private Function IComparerOfString_Compare(x As String, y As String) As Integer Implements IComparer(Of String).Compare
        Return StrCmpLogical(x, y)
    End Function
End Class

Class Comparer(Of T)
    Implements IComparer(Of T)

    Property PropName As String
    Property Ascending As Boolean = True

    Sub New(propName As String, Optional ascending As Boolean = True)
        Me.PropName = propName
        Me.Ascending = ascending
    End Sub

    Public Function Compare(x As T, y As T) As Integer Implements IComparer(Of T).Compare
        If Not Ascending Then
            Dim x1 = x
            x = y
            y = x1
        End If

        Dim type = x.GetType
        Dim propInfo = type.GetProperty(PropName)

        Return DirectCast(propInfo.GetValue(x), IComparable).CompareTo(propInfo.GetValue(y))
    End Function
End Class