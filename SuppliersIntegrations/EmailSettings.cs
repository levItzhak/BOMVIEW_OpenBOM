﻿using System;
using System.Windows.Controls;

namespace BOMVIEW.Models
{
    public class EmailSettings
    {
        public string SmtpServer { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string FromEmail { get; set; }
        public string Password { get; set; }
        
        public string ToEmail { get; set; } = "levi @testview.co.il";
        public bool EnableSsl { get; set; } = true;
    }
}