using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Domain.Entities.Users
{
    public sealed class User
    {
        private User(string id, string name, string phone, string passwordHash)
        {
            Id = id;
            Name = name;
            Phone = phone;
            PasswordHash = passwordHash;
            IsActived = true;
        }

        public string Id { get; }
        public string Name { get; private set; }
        public string Phone { get; private set; }
        public string PasswordHash { get; private set; }
        public bool IsActived { get; private set; }

        //  回填
        public static User Rehydrate(string id, string name, string phone, string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("User Id 必填");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("姓名必填");
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("手機必填");
            if (string.IsNullOrWhiteSpace(passwordHash)) throw new InvalidOperationException("密碼必填");

            return new User(id, name.Trim(), phone.Trim(), passwordHash.Trim());
        }

        public static User Register(string name, string phone, string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("姓名必填");
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("手機必填");
            if (string.IsNullOrWhiteSpace(passwordHash)) throw new InvalidOperationException("密碼必填");

            return new User(string.Empty, name.Trim(), phone.Trim(), passwordHash.Trim());
        }

        public void DeActive()
        {
            IsActived = false;
        }
    }
}
