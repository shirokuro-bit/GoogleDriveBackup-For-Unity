using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class GoogleDriveBackup : EditorWindow
{
    bool IsDetail = false;
    bool IsExit = false;

    private string PROJECT_NAME;
    private string archiveFileName;

    // Folder Paths
    private string PROJECT_PATH;
    private string TEMP_PATH;

    private string targetPath;
    private string archiveFilePath;

    [MenuItem("Window/GoogleDriveBackup")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<GoogleDriveBackup>(typeof(GoogleDriveBackup));
    }

    void Initialize()
    {
        PROJECT_NAME = Application.productName;
        PROJECT_PATH = Path.GetDirectoryName(Application.dataPath);
        TEMP_PATH = Application.temporaryCachePath;

        archiveFileName = PROJECT_NAME + ".zip";
        archiveFilePath = Path.Combine(TEMP_PATH, archiveFileName);
    }

    void OnGUI()
    {
        Initialize();
        // Project Folder Path
        GUILayout.Label("Project Folder Path", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(PROJECT_PATH);

        GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));

        // Temp Folder Path
        GUILayout.Label("Temp Folder Path", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(TEMP_PATH);

        GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));

        // Process Setting
        GUILayout.Label("Process Setting", EditorStyles.boldLabel);
        IsDetail = EditorGUILayout.Toggle("Show Detail", IsDetail);
        IsExit = EditorGUILayout.Toggle("Save on exit", IsExit);

        if (GUILayout.Button("Save and Backup"))
        {
            SaveData();

            if (CreateArchive() != 0)
            {
                return;
            }
            Upload();

            // Exit Unity if isExit is truely 
            if (IsExit)
            {
                EditorApplication.Exit(0);
            }
        }
    }

    private void SaveData()
    {
        // Save Data
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        ShowDitail("Save Complete");
    }

    private int CreateArchive()
    {
        // Folder Paths
        targetPath = Path.Combine(TEMP_PATH, PROJECT_NAME);

        // Check old data
        if (File.Exists(archiveFilePath))
        {
            Debug.LogWarning("ZipFile is already exists.");
            return 1;
        }
        if (Directory.Exists(targetPath))
        {
            Debug.LogWarning("Directory is already exists.");
            return 1;
        }

        // Exclude directry Temp

        Directory.CreateDirectory(targetPath);

        string[] projectFiles = Directory.GetFileSystemEntries(PROJECT_PATH);
        projectFiles = projectFiles.Where(e => !e.Contains("Temp")).ToArray();

        foreach (string projectFile in projectFiles)
        {
            string path = Path.Combine(targetPath, Path.GetFileName(projectFile));
            FileUtil.CopyFileOrDirectory(projectFile, path);
        }

        ShowDitail("Copy Complete");

        ZipFile.CreateFromDirectory(targetPath, archiveFilePath);

        ShowDitail("Archive Complete");
        return 0;
    }

    private Task<UserCredential> GetUserCredential()
    {
        string CLIENT_SECRET_PATH = "Packages/com.github.shirokuro-bit.googledrivebackup_for_unity/Editor/client_secret.json";
        string CRED_PATH = Path.Combine(PROJECT_PATH, "Assets/authorize.token");
        string[] scope = new string[] { DriveService.ScopeConstants.Drive };

        return GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromFile(CLIENT_SECRET_PATH).Secrets,
                scope,
                "user",
                System.Threading.CancellationToken.None,
                new FileDataStore(CRED_PATH, true)
        );
    }
    private async void Upload()
    {
        string mimeType = "application/zip";
        ICredential credential = await GetUserCredential();

        DriveService driveService = new DriveService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = PROJECT_NAME
        });

        // Get file list from GoogleDrive.
        FilesResource.ListRequest listRequest = driveService.Files.List();
        listRequest.PageSize = 10;
        listRequest.Fields = "nextPageToken, files(id, name)";

        IUploadProgress prog;
        using (FileStream uploadData = new FileStream(archiveFilePath, FileMode.Open))
        {
            // Create file meta data.
            Google.Apis.Drive.v3.Data.File fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = archiveFileName,
                MimeType = mimeType,
            };

            // Extract archiveFileName
            IEnumerable<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute().Files.Where(file => file.Name.Equals(archiveFileName));

            // If the file does not exist, create a new one.
            if (files == null || !files.Any())
            {
                FilesResource.CreateMediaUpload req = driveService.Files.Create(fileMetadata, uploadData, mimeType);
                req.Fields = "id, name";
                prog = req.Upload();

                if (prog.Status == UploadStatus.Completed)
                {
                    var uploadedFile = req.ResponseBody;
                    ShowDitail($"Create file with ID: {uploadedFile.Id}");
                }
                else
                {
                    ShowDitail($"Upload failed: {prog.Exception}");
                }
            }

            // If the file exists, update it.
            else
            {
                FilesResource.UpdateMediaUpload req = driveService.Files.Update(fileMetadata, files.First().Id, uploadData, mimeType);
                req.Fields = "id, name";
                prog = req.Upload();

                if (prog.Status == UploadStatus.Completed)
                {
                    var uploadedFile = req.ResponseBody;
                    ShowDitail($"Update file with ID: {uploadedFile.Id}");
                }
                else
                {
                    ShowDitail($"Upload failed: {prog.Exception}");
                }
            }
        }
        FinalizeData();
    }

    private void FinalizeData()
    {
        // Delete Source Data
        FileUtil.DeleteFileOrDirectory(targetPath);
        FileUtil.DeleteFileOrDirectory(archiveFilePath);

        ShowDitail("Completed");
    }

    private void ShowDitail(string message)
    {
        if (IsDetail)
        {
            Debug.Log(message);
        }

    }
}
