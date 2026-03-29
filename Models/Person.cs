using Sati.Models;
using System;
using System.Collections.Generic;
using System.Security.Permissions;
using System.Text;


namespace Sati
{
    public class Person
    {

        //simple properties
        public int Id { get; private set; }
        public int UserId { get; private set;  }
        public string? FirstName { get;  set; }
        public string? LastName { get; set; } 
        public DateTime BirthDate { get;  set; }
        public DateTime EffectiveDate { get;  set; }
        public string? Bio { get;  set; }
        public WaiverType Waiver { get;  set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();

        //collections
        public List<Form> Forms { get; set; } = new List<Form>();
        public List<Note> Notes { get; set; } = new List<Note>();

        //constructor
        protected Person() { }

        //methods
        public static Person CreatePerson(int userId, string firstName, string lastName, string Bio, DateTime birthdate, DateTime effective, WaiverType waiver) 
        {
            var person = new Person
            {
                UserId = userId,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Bio = Bio.Trim(),
                BirthDate = birthdate,
                EffectiveDate = effective,
                Waiver = waiver
            };

                var forms = GenerateFormList(person.EffectiveDate);
                person.Forms = forms;
            
            return person;
        }

        public static List<Form> GenerateFormList(DateTime effective)
        {
            var list = new List<Form>();
            list.Add(new Form { DueDate = effective.AddDays(90), Type = FormType.Q1R, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(180), Type = FormType.Q2R, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.Q3R, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Q4R, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.PCP, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.Reclassification, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.ComprehensiveAssessment, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_Agency, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_DHHS, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_Medical, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.SafetyPlan, IsCompliant = true });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.PrivacyPractices, IsCompliant = true });

            return list;
        }

        public Form? GetCurrentCycleForm(FormType type)
        {
            var today = DateTime.Today;
            var yearsElapsed = today.Year - EffectiveDate.Year;
            if (today < EffectiveDate.AddYears(yearsElapsed))
                yearsElapsed--;

            var cycleStart = EffectiveDate.AddYears(yearsElapsed);
            var cycleEnd = EffectiveDate.AddYears(yearsElapsed + 1);

            var currentCycle = Forms
                .Where(f => f.Type == type &&
                            f.DueDate >= cycleStart &&
                            f.DueDate < cycleEnd)
                .OrderByDescending(f => f.DueDate)
                .FirstOrDefault();

            if (currentCycle is not null)
                return currentCycle;

            // fallback — return most recent form of this type
            return Forms
                .Where(f => f.Type == type)
                .OrderByDescending(f => f.DueDate)
                .FirstOrDefault();
        }
    }
}
