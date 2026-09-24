Option Explicit

If WScript.Arguments.Count <> 1 Then
    WScript.Quit 64
End If

Dim scriptName
scriptName = WScript.Arguments(0)
If InStr(scriptName, "\") > 0 Or InStr(scriptName, "/") > 0 Or InStr(scriptName, "..") > 0 Then
    WScript.Quit 64
End If

Dim shell, fileSystem, scriptPath, powershellPath, command, exitCode
Dim installerTestMode, installerResultPath, installerResultTempPath, resultFile
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
scriptPath = fileSystem.BuildPath(fileSystem.GetParentFolderName(WScript.ScriptFullName), scriptName)
If Not fileSystem.FileExists(scriptPath) Then
    WScript.Quit 2
End If

powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
command = Chr(34) & powershellPath & Chr(34) & _
    " -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " & _
    Chr(34) & scriptPath & Chr(34)
exitCode = shell.Run(command, 0, True)

' IExpress may return from its outer extraction process before the launched command
' finishes. In isolated installer-test mode only, record the authoritative PowerShell
' installer exit code so acceptance can verify refusal without relying on that wrapper.
installerTestMode = shell.ExpandEnvironmentStrings("%SATI_DEMO_INSTALLER_TEST%")
installerResultPath = shell.ExpandEnvironmentStrings("%SATI_INSTALLER_TEST_RESULT_PATH%")
If installerTestMode = "1" And _
   installerResultPath <> "%SATI_INSTALLER_TEST_RESULT_PATH%" And _
   Len(installerResultPath) > 0 Then
    installerResultTempPath = installerResultPath & ".tmp"
    Set resultFile = fileSystem.CreateTextFile(installerResultTempPath, False, False)
    resultFile.Write CStr(exitCode)
    resultFile.Close
    fileSystem.MoveFile installerResultTempPath, installerResultPath
End If

WScript.Quit exitCode
