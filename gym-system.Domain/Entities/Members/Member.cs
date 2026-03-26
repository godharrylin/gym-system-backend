using System.ComponentModel.DataAnnotations;

namespace gym_system.Domain.Entities.Members
{
    //  基於業務領域的 「學員/會員」的概念
    public class Member
    {
        // 基本屬性 (可能存入 user 表)
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Phone { get; private set; }
        public string PasswordHash { get; private set; }

        [Display(Name = "會員啟用狀態")]
        public bool IsActived { get; private set; }

        //  會員狀態 (可能存入 sdt_profile 表)
        [Display(Name = "最近一次入場時間")]
        public DateTime? LastVisitDate { get; set; }

        [Display(Name = "當前票券啟用狀態")]
        public string? CurrentTicketStatus { get; set; }    //  e.g., UnActive, Active

        //  建構子
        private Member(string id, string name, string phone, string passwordHash) 
        {
            Id = id;
            Name = name;
            Phone = phone;
            PasswordHash = passwordHash;
            IsActived = true;
            LastVisitDate = null;
            CurrentTicketStatus = "UnActive";
        }


        public static Member Register(string id, string name, string phone)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("姓名必填");
            if (string.IsNullOrWhiteSpace(phone)) throw new Exception("手機必填");

            return new Member(id, name, phone, phone);
        }

    }
}
