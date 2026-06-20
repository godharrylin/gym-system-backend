using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Domain.Entities.Users
{
    public sealed class User
    {
        private User(string id, string name, string phone, string password)
        {
            Id = id;
            Name = name;
            Phone = phone;
            Password = password;
            IsActive = true;
        }

        private User(string id, string name, string phone, string password, bool isActive)
        {
            Id = id;
            Name = name;
            Phone = phone;
            Password = password;
            IsActive = isActive;
        }

        public string Id { get; }
        public string Name { get; private set; }
        public string Phone { get; private set; }
        public string Password { get; private set; }
        public bool IsActive { get; private set; }

        //  回填
        public static User Rehydrate(string id, string name, string phone, string password, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("User Id 必填");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("姓名必填");
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("手機必填");
            if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("密碼必填");

            return new User(id, name.Trim(), phone.Trim(), password.Trim(), isActive);
        }

        public static User Register(string name, string phone, string password)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("姓名必填");
            if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("手機必填");
            if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("密碼必填");

            return new User(string.Empty, name.Trim(), phone.Trim(), password.Trim());
        }

        public void Deactivate()
        {
            IsActive = false;
        }
    }
}
