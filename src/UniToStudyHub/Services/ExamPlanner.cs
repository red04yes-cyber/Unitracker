using UniToStudyHub.Models;
namespace UniToStudyHub.Services;
public static class ExamPlanner
{
 private sealed record SessionTarget(string Name,DateTime AnchorDate);
 public static void AssignIndicativeSessions(IList<Exam> exams,DateTime? referenceDate=null,int maxCfuPerSession=24,DateTime? targetDate=null) { var now=(referenceDate??DateTime.Today).Date; var deadline=(targetDate??now.AddYears(2)).Date; if(deadline<now)deadline=now.AddYears(1); var sessions=BuildUpcomingSessions(now,deadline); if(sessions.Count==0)sessions=BuildUpcomingSessions(now,now.AddYears(1)); var loads=sessions.ToDictionary(s=>s.Name,_=>0); foreach(var exam in exams.Where(e=>!e.Completed).OrderByDescending(e=>e.Cfu).ThenByDescending(e=>e.EstimatedStudyHours)){var chosen=sessions.OrderBy(s=>loads[s.Name]>=maxCfuPerSession?1:0).ThenBy(s=>loads[s.Name]).ThenBy(s=>s.AnchorDate).First();exam.PlannedSession=chosen.Name;exam.PlannedDate=chosen.AnchorDate;loads[chosen.Name]+=Math.Max(0,exam.Cfu);}}
 private static List<SessionTarget> BuildUpcomingSessions(DateTime today,DateTime target){var result=new List<SessionTarget>();for(var year=today.Year;year<=target.Year;year++){AddIfFuture(result,"Invernale",new DateTime(year,1,20),today);AddIfFuture(result,"Estiva",new DateTime(year,6,20),today);AddIfFuture(result,"Autunnale",new DateTime(year,9,15),today);}return result.Where(s=>s.AnchorDate<=target).OrderBy(s=>s.AnchorDate).ToList();}
 private static void AddIfFuture(List<SessionTarget> list,string name,DateTime date,DateTime today){if(date>=today)list.Add(new SessionTarget($"{name} {date.Year}",date));}
}