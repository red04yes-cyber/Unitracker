using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;
using UniToStudyHub.Presentation.Commands;
using UniToStudyHub.Services;

namespace UniToStudyHub.Presentation.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IStudyDataStore _store;
    private readonly MoodleApplicationService _moodle;
    private readonly CareerCalculatorService _calculator;
    private StudyData _data = new();
    private Course? _selectedGradeCourse;
    private string _courseNameInput = string.Empty;
    private string _courseCfuInput = "6";
    private string _gradeValueInput = "28";
    private string _moodleUrlInput = string.Empty;
    private string _moodleToken = string.Empty;
    private string _moodleStatus = "Non collegato";
    private string _averageDisplay = "—";
    private string _baseGradeDisplay = "Voto di partenza: —";
    private string _creditsDisplay = "CFU completati: 0";
    private string _statusMessage = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<Course> Courses => _data.Courses;
    public ObservableCollection<Grade> Grades => _data.Grades;
    public ObservableCollection<Exam> Exams => _data.Exams;
    public ObservableCollection<Announcement> Announcements => _data.Announcements;

    public string CourseNameInput { get => _courseNameInput; set => Set(ref _courseNameInput, value); }
    public string CourseCfuInput { get => _courseCfuInput; set => Set(ref _courseCfuInput, value); }
    public string GradeValueInput { get => _gradeValueInput; set => Set(ref _gradeValueInput, value); }
    public Course? SelectedGradeCourse { get => _selectedGradeCourse; set => Set(ref _selectedGradeCourse, value); }
    public string MoodleUrlInput { get => _moodleUrlInput; set => Set(ref _moodleUrlInput, value); }
    public string MoodleToken { get => _moodleToken; set => Set(ref _moodleToken, value); }
    public string MoodleStatus { get => _moodleStatus; private set => Set(ref _moodleStatus, value); }
    public string AverageDisplay { get => _averageDisplay; private set => Set(ref _averageDisplay, value); }
    public string BaseGradeDisplay { get => _baseGradeDisplay; private set => Set(ref _baseGradeDisplay, value); }
    public string CreditsDisplay { get => _creditsDisplay; private set => Set(ref _creditsDisplay, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    public AsyncRelayCommand AddCourseCommand { get; }
    public AsyncRelayCommand AddGradeCommand { get; }
    public AsyncRelayCommand PlanExamsCommand { get; }
    public AsyncRelayCommand ConnectMoodleCommand { get; }
    public AsyncRelayCommand SyncMoodleCommand { get; }

    public MainViewModel(IStudyDataStore store, MoodleApplicationService moodle, CareerCalculatorService calculator)
    {
        _store = store;
        _moodle = moodle;
        _calculator = calculator;
        AddCourseCommand = new AsyncRelayCommand(AddCourseAsync);
        AddGradeCommand = new AsyncRelayCommand(AddGradeAsync, () => SelectedGradeCourse is not null);
        PlanExamsCommand = new AsyncRelayCommand(PlanExamsAsync);
        ConnectMoodleCommand = new AsyncRelayCommand(ConnectMoodleAsync);
        SyncMoodleCommand = new AsyncRelayCommand(SyncMoodleAsync);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _data = await _store.LoadAsync(cancellationToken);
        MoodleUrlInput = _data.MoodleBaseUrl;
        RefreshCollections();
        RefreshCareerSummary();
    }

    private async Task AddCourseAsync()
    {
        if (string.IsNullOrWhiteSpace(CourseNameInput) || !int.TryParse(CourseCfuInput, out var cfu) || cfu <= 0)
        {
            StatusMessage = "Inserisci un nome e un numero di CFU valido.";
            return;
        }

        var course = new Course { Name = CourseNameInput.Trim(), Cfu = cfu };
        Courses.Add(course);
        try
        {
            await SaveAsync();
            CourseNameInput = string.Empty;
            StatusMessage = "Corso aggiunto.";
        }
        catch (Exception ex)
        {
            Courses.Remove(course);
            StatusMessage = $"Salvataggio non riuscito: {ex.Message}";
        }
        RefreshCareerSummary();
    }

    private async Task AddGradeAsync()
    {
        if (SelectedGradeCourse is null || !int.TryParse(GradeValueInput, out var grade) || grade is < 18 or > 30)
        {
            StatusMessage = "Seleziona un corso e inserisci un voto tra 18 e 30.";
            return;
        }

        var course = SelectedGradeCourse;
        var item = new Grade
        {
            CourseId = course.Id, CourseName = course.Name, Cfu = course.Cfu,
            Value = grade, Date = DateTime.Today, CountsTowardAverage = true
        };
        Grades.Add(item);
        var previousCompleted = course.Completed;
        course.Completed = true;
        try
        {
            await SaveAsync();
            StatusMessage = "Voto registrato.";
        }
        catch (Exception ex)
        {
            Grades.Remove(item);
            course.Completed = previousCompleted;
            StatusMessage = $"Salvataggio non riuscito: {ex.Message}";
        }
        RefreshCareerSummary();
    }

    private async Task PlanExamsAsync()
    {
        try
        {
            ExamPlanner.AssignIndicativeSessions(_data.Exams, targetDate: _data.GraduationTarget);
            await SaveAsync();
            OnPropertyChanged(nameof(Exams));
            StatusMessage = "Pianificazione aggiornata. Le date sono indicative.";
        }
        catch (Exception ex) { StatusMessage = $"Pianificazione non riuscita: {ex.Message}"; }
    }

    private async Task ConnectMoodleAsync()
    {
        try
        {
            var siteName = await _moodle.VerifyAndStoreTokenAsync(MoodleUrlInput, MoodleToken);
            _data.MoodleBaseUrl = MoodleUrlInput.Trim();
            await SaveAsync();
            MoodleStatus = $"Collegato: {siteName}";
            StatusMessage = "Token verificato e salvato in Windows DPAPI.";
        }
        catch (Exception ex) { MoodleStatus = ex.Message; }
    }

    private async Task SyncMoodleAsync()
    {
        try
        {
            var courses = await _moodle.GetEnrolledCoursesAsync(MoodleUrlInput);
            var added = 0;
            foreach (var remote in courses)
            {
                var local = Courses.FirstOrDefault(c => c.MoodleCourseId == remote.id);
                if (local is null)
                {
                    Courses.Add(new Course { MoodleCourseId = remote.id, Name = remote.fullname, Code = remote.shortname });
                    added++;
                }
            }
            _data.MoodleBaseUrl = MoodleUrlInput.Trim();
            await SaveAsync();
            MoodleStatus = $"Sincronizzati {courses.Count} corsi Moodle ({added} nuovi).";
            OnPropertyChanged(nameof(Courses));
        }
        catch (Exception ex) { MoodleStatus = ex.Message; }
    }

    private async Task SaveAsync(CancellationToken cancellationToken = default) =>
        await _store.SaveAsync(_data, cancellationToken);

    private void RefreshCollections()
    {
        OnPropertyChanged(nameof(Courses));
        OnPropertyChanged(nameof(Grades));
        OnPropertyChanged(nameof(Exams));
        OnPropertyChanged(nameof(Announcements));
    }

    private void RefreshCareerSummary()
    {
        AverageDisplay = GradeCalculator.Format(GradeCalculator.WeightedAverage(_data.Grades, _calculator));
        BaseGradeDisplay = "Voto di partenza: " + GradeCalculator.Format(GradeCalculator.ProjectedBase(_data.Grades, _calculator));
        CreditsDisplay = $"CFU completati: {GradeCalculator.AcquiredCfu(_data.Grades, _calculator)}";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(SelectedGradeCourse)) AddGradeCommand?.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}