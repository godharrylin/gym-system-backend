using System;
using System.Collections.Generic;
using System.Text;
using gym_system.Domain.Entities.Members;

namespace gym_system.Domain.Repositories
{
    public interface IMemberRepository
    {
        /// <summary>
        ///  新增會員
        /// </summary>
        /// <param name="member"></param>
        /// <returns></returns>
        public Task AddAsync(Member member);

        /// <summary>
        /// 驗證電話號碼是否重複
        /// </summary>
        public Task<bool> CheckPhoneExistAsync(string phone);
        
    }
}
