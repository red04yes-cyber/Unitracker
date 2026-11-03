using UniToStudyHub.Models;
namespace UniToStudyHub.Services;
public static class GradeCalculator
{
 public static decimal? WeightedAverage(IEnumerable<Grade> grades) { var valid=grades.Where(g=>g.CountsTowardAverage&&g.Cfu>0&&g.Value is >=18 and <=30).ToList(); var total=valid.Sum(g=>g.Cfu); return total==0?null:valid.Sum(g=>(decimal)g.Value*g.Cfu)/total; }
 public static decimal? ProjectedBase(IEnumerable<Grade> grades) { var a=WeightedAverage(grades); return a is null?null:a.Value*110m/30m; }
 public static decimal? Simulate(IEnumerable<Grade> grades,Course course,int hypotheticalGrade,bool honors) { if(hypotheticalGrade is <18 or >30||course.Cfu<=0)return WeightedAverage(grades); var copy=grades.Where(g=>g.CourseId!=course.Id).ToList(); copy.Add(new Grade{CourseId=course.Id,CourseName=course.Name,Value=hypotheticalGrade,Honors=honors,Cfu=course.Cfu,CountsTowardAverage=true}); return WeightedAverage(copy); }
 public static string Format(decimal? value,string suffix="")=>value is null?"—":value.Value.ToString("0.00")+suffix;
}