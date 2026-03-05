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
	"CreditLimit" DECIMAL(10, 2) NULL,
    "CreatedBy" UUID NULL,
	"CreatedDate" TIMESTAMPTZ NULL, 
	"ModifiedBy" UUID NULL,
	"ModifiedDate" TIMESTAMPTZ NULL, 
	"Cix" INT GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Base_Auth_User" PRIMARY KEY ("Id")
);

CREATE INDEX "CIX_Base_Auth_User" ON "Base_Auth_User" ("Cix");