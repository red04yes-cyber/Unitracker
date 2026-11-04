using System.IO;
using System.Text.Json;
using UniToStudyHub.Models;
namespace UniToStudyHub.Services;
public sealed class LocalDataStore
{
 private readonly SemaphoreSlim _gate=new(1,1); private readonly string _dataPath; private readonly JsonSerializerOptions _options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
 public LocalDataStore(){var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UniToStudyHub");Directory.CreateDirectory(folder);_dataPath=Path.Combine(folder,"data.json");}
 public async Task<StudyData> LoadAsync(CancellationToken cancellationToken=default){await _gate.WaitAsync(cancellationToken);try{if(!File.Exists(_dataPath))return new StudyData();await using var stream=File.OpenRead(_dataPath);return await JsonSerializer.DeserializeAsync<StudyData>(stream,_options,cancellationToken)??new StudyData();}catch(JsonException){var backup=_dataPath+".invalid-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");File.Move(_dataPath,backup,overwrite:true);return new StudyData();}finally{_gate.Release();}}
 public async Task SaveAsync(StudyData data,CancellationToken cancellationToken=default){await _gate.WaitAsync(cancellationToken);var temp=_dataPath+".tmp";try{await using(var stream=File.Create(temp))await JsonSerializer.SerializeAsync(stream,data,_options,cancellationToken);File.Move(temp,_dataPath,overwrite:true);}finally{if(File.Exists(temp))File.Delete(temp);_gate.Release();}}
}