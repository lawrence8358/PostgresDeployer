CREATE TABLE "Base_Auth_User"
(
	"Id" UUID NOT NULL,
	"Account" VARCHAR(200) NOT NULL,
    "Name" VARCHAR(500) NULL,
    "Alias" VARCHAR(500) NULL,
    "IdNo" VARCHAR(50) NULL,
    "PhotoFgId" UUID NULL,
    "Email" VARCHAR(500) NULL,
    "VerifiedEmail" VARCHAR(500) NULL,
    "AdId" VARCHAR(200) NULL,
    "AdDn" VARCHAR(2000) NULL,
    "IsVerifiedEmail" BOOLEAN NOT NULL DEFAULT FALSE,
	"VerifyEmaiCode" VARCHAR(2000) NULL,
	"Type" SMALLINT NOT NULL,
	"PasswordHash" VARCHAR(200) NULL,
	"ForgotPasswordCode" VARCHAR(2000) NULL,
    "Active" BOOLEAN NOT NULL DEFAULT FALSE,
    "Tel" VARCHAR(200) NULL,
	"LastSigninDate" TIMESTAMPTZ NULL,
    "LastIpAddress" VARCHAR(200) NULL,
    "FailureCount" SMALLINT NOT NULL DEFAULT 0,
    "LockDate" TIMESTAMPTZ NULL,
    "IsForceChangePassword" BOOLEAN NOT NULL DEFAULT FALSE,
    "IgnorePwdRule" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedBy" UUID NULL,
	"CreatedDate" TIMESTAMPTZ NULL,
	"ModifiedBy" UUID NULL,
	"ModifiedDate" TIMESTAMPTZ NULL,
	"Cix" INT GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Base_Auth_User" PRIMARY KEY ("Id")
);

CREATE INDEX "CIX_Base_Auth_User" ON "Base_Auth_User" ("Cix");

COMMENT ON TABLE "Base_Auth_User" IS '授權角色資料表';

COMMENT ON COLUMN "Base_Auth_User"."Id" IS '使用者識別碼';
COMMENT ON COLUMN "Base_Auth_User"."Account" IS '帳號，須配合認證類型帳號，第三方登入此欄位為系統自動產生';
COMMENT ON COLUMN "Base_Auth_User"."Name" IS '姓名';
COMMENT ON COLUMN "Base_Auth_User"."Alias" IS '別名';
COMMENT ON COLUMN "Base_Auth_User"."IdNo" IS '身分證/居留證號';
COMMENT ON COLUMN "Base_Auth_User"."PhotoFgId" IS '使用者照片 FileGroupd Id';
COMMENT ON COLUMN "Base_Auth_User"."Email" IS '電子郵件';
COMMENT ON COLUMN "Base_Auth_User"."VerifiedEmail" IS '通過驗證的電子郵件(第三方登入用)';
COMMENT ON COLUMN "Base_Auth_User"."AdId" IS '同步當下的 AD 識別值';
COMMENT ON COLUMN "Base_Auth_User"."AdDn" IS '同步當下的 AD Distinguished Name';
COMMENT ON COLUMN "Base_Auth_User"."IsVerifiedEmail" IS '電子郵件是否已驗證';
COMMENT ON COLUMN "Base_Auth_User"."VerifyEmaiCode" IS '電子郵件驗證碼，JSON';
COMMENT ON COLUMN "Base_Auth_User"."Type" IS '認證類型，站內認證: 1, AD認證: 5, 第三方登入: 10';
COMMENT ON COLUMN "Base_Auth_User"."PasswordHash" IS '密碼，站內認證使用';
COMMENT ON COLUMN "Base_Auth_User"."ForgotPasswordCode" IS '忘記密碼驗證碼，JSON';
COMMENT ON COLUMN "Base_Auth_User"."Active" IS '是否啟用';
COMMENT ON COLUMN "Base_Auth_User"."Tel" IS '電話號碼';
COMMENT ON COLUMN "Base_Auth_User"."LastSigninDate" IS '最後登入時間';
COMMENT ON COLUMN "Base_Auth_User"."LastIpAddress" IS '最後存取 IP';
COMMENT ON COLUMN "Base_Auth_User"."FailureCount" IS '登入錯誤次數';
COMMENT ON COLUMN "Base_Auth_User"."LockDate" IS '鎖定時間';
COMMENT ON COLUMN "Base_Auth_User"."IsForceChangePassword" IS '是否強制變更密碼';
COMMENT ON COLUMN "Base_Auth_User"."IgnorePwdRule" IS '忽略密碼規則';
COMMENT ON COLUMN "Base_Auth_User"."CreatedBy" IS '建立人員識別碼';
COMMENT ON COLUMN "Base_Auth_User"."CreatedDate" IS '建立日期';
COMMENT ON COLUMN "Base_Auth_User"."ModifiedBy" IS '修改人員識別碼';
COMMENT ON COLUMN "Base_Auth_User"."ModifiedDate" IS '修改日期';
COMMENT ON COLUMN "Base_Auth_User"."Cix" IS '叢集索引鍵';