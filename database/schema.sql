/* =====================================================================
   AI Business Assistant - SQL Server schema
   Use this script only if you set  Database__AutoCreate=false.
   By default the API creates this same schema itself on first start
   (EF Core EnsureCreated), so do NOT run both against one database.
   ===================================================================== */

IF DB_ID(N'BizAssistant') IS NULL
    CREATE DATABASE BizAssistant;
GO

USE BizAssistant;
GO

CREATE TABLE dbo.BusinessProfiles (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(200)  NOT NULL,
    Description NVARCHAR(2000) NOT NULL,
    Tone        NVARCHAR(100)  NOT NULL,
    Faq         NVARCHAR(4000) NOT NULL,
    LogoDataUrl NVARCHAR(MAX)  NULL
);

/* Registered logins. Each user belongs to one business (BusinessProfiles.Id).
   The admin from configuration is not stored here. */
CREATE TABLE dbo.Users (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Email        NVARCHAR(320) NOT NULL,
    PasswordHash NVARCHAR(400) NOT NULL,
    BusinessId   INT           NOT NULL,
    CreatedAt    DATETIME2     NOT NULL
);
CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email);

CREATE TABLE dbo.Leads (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    BusinessId INT           NOT NULL,
    Name      NVARCHAR(200)  NOT NULL,
    Email     NVARCHAR(320)  NOT NULL,
    Phone     NVARCHAR(50)   NULL,
    Source    NVARCHAR(100)  NOT NULL,
    Stage     NVARCHAR(20)   NOT NULL CONSTRAINT DF_Leads_Stage DEFAULT N'New',
    Score     INT            NOT NULL CONSTRAINT DF_Leads_Score DEFAULT 0,
    Notes     NVARCHAR(2000) NULL,
    CreatedAt DATETIME2      NOT NULL CONSTRAINT DF_Leads_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2      NOT NULL CONSTRAINT DF_Leads_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Leads_Stage CHECK (Stage IN (N'New', N'Contacted', N'Qualified', N'Won', N'Lost')),
    CONSTRAINT CK_Leads_Score CHECK (Score BETWEEN 0 AND 100)
);
CREATE UNIQUE INDEX IX_Leads_BusinessId_Email ON dbo.Leads (BusinessId, Email);
CREATE INDEX IX_Leads_Stage ON dbo.Leads (Stage);

CREATE TABLE dbo.Inquiries (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    BusinessId    INT            NOT NULL,
    CustomerName  NVARCHAR(200)  NOT NULL,
    CustomerEmail NVARCHAR(320)  NOT NULL,
    Channel       NVARCHAR(50)   NOT NULL,
    Subject       NVARCHAR(300)  NOT NULL,
    Message       NVARCHAR(4000) NOT NULL,
    Category      NVARCHAR(20)   NULL,
    Sentiment     NVARCHAR(20)   NULL,
    Urgency       NVARCHAR(20)   NULL,
    Summary       NVARCHAR(1000) NULL,
    Status        NVARCHAR(20)   NOT NULL CONSTRAINT DF_Inquiries_Status DEFAULT N'New',
    ReceivedAt    DATETIME2      NOT NULL CONSTRAINT DF_Inquiries_ReceivedAt DEFAULT SYSUTCDATETIME(),
    LeadId        INT            NULL,
    CONSTRAINT FK_Inquiries_Leads FOREIGN KEY (LeadId) REFERENCES dbo.Leads (Id) ON DELETE SET NULL,
    CONSTRAINT CK_Inquiries_Status    CHECK (Status    IN (N'New', N'Triaged', N'Replied', N'Closed')),
    CONSTRAINT CK_Inquiries_Category  CHECK (Category  IS NULL OR Category  IN (N'Sales', N'Support', N'Billing', N'Partnership', N'Spam', N'Other')),
    CONSTRAINT CK_Inquiries_Sentiment CHECK (Sentiment IS NULL OR Sentiment IN (N'Positive', N'Neutral', N'Negative')),
    CONSTRAINT CK_Inquiries_Urgency   CHECK (Urgency   IS NULL OR Urgency   IN (N'Low', N'Medium', N'High'))
);
CREATE INDEX IX_Inquiries_BusinessId ON dbo.Inquiries (BusinessId);
CREATE INDEX IX_Inquiries_Status_ReceivedAt ON dbo.Inquiries (Status, ReceivedAt);
CREATE INDEX IX_Inquiries_LeadId ON dbo.Inquiries (LeadId);

CREATE TABLE dbo.Replies (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    InquiryId  INT            NOT NULL,
    Body       NVARCHAR(4000) NOT NULL,
    IsAiDraft  BIT            NOT NULL CONSTRAINT DF_Replies_IsAiDraft DEFAULT 0,
    Status     NVARCHAR(20)   NOT NULL CONSTRAINT DF_Replies_Status DEFAULT N'Draft',
    CreatedAt  DATETIME2      NOT NULL CONSTRAINT DF_Replies_CreatedAt DEFAULT SYSUTCDATETIME(),
    SentAt     DATETIME2      NULL,
    CONSTRAINT FK_Replies_Inquiries FOREIGN KEY (InquiryId) REFERENCES dbo.Inquiries (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Replies_Status CHECK (Status IN (N'Draft', N'Sent'))
);
CREATE INDEX IX_Replies_InquiryId ON dbo.Replies (InquiryId);

CREATE TABLE dbo.FollowUps (
    Id      INT IDENTITY(1,1) PRIMARY KEY,
    LeadId  INT            NOT NULL,
    DueAt   DATETIME2      NOT NULL,
    Message NVARCHAR(4000) NOT NULL,
    Status  NVARCHAR(20)   NOT NULL CONSTRAINT DF_FollowUps_Status DEFAULT N'Pending',
    SentAt  DATETIME2      NULL,
    CONSTRAINT FK_FollowUps_Leads FOREIGN KEY (LeadId) REFERENCES dbo.Leads (Id) ON DELETE CASCADE,
    CONSTRAINT CK_FollowUps_Status CHECK (Status IN (N'Pending', N'Sent', N'Cancelled'))
);
CREATE INDEX IX_FollowUps_Status_DueAt ON dbo.FollowUps (Status, DueAt);
GO

INSERT INTO dbo.BusinessProfiles (Name, Description, Tone, Faq)
VALUES (
    N'Sample Business',
    N'A small local business. Edit this profile so the AI knows what you sell.',
    N'Friendly and professional',
    N'Opening hours: Mon-Fri 9am-5pm. Replies within one business day.'
);
GO
