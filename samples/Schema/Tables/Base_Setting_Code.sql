CREATE TABLE "Base_Setting_Code"
( 
	"Id" UUID NOT NULL, 
    "TypeId" UUID NOT NULL, 
    "Code" VARCHAR(50) NOT NULL, 
    "Name" VARCHAR(200) NOT NULL,
    "Seq" INT NULL,
    "IsSystem" BOOLEAN NOT NULL DEFAULT FALSE,
    "Enabled" BOOLEAN NOT NULL DEFAULT TRUE,  
    "Value" VARCHAR(200) NULL,  
    "OptionValue" TEXT NULL, 
    "CreatedBy" UUID NULL,
	"CreatedDate" TIMESTAMPTZ NOT NULL,
	"ModifiedBy" UUID NULL,
	"ModifiedDate" TIMESTAMPTZ NULL, 
	"Cix" INT GENERATED ALWAYS AS IDENTITY NOT NULL, 
    CONSTRAINT "PK_Base_Setting_Code" PRIMARY KEY ("Id") 
);

CREATE INDEX "CIX_Base_Setting_Code" ON "Base_Setting_Code" ("Cix");