CREATE TABLE "Base_Setting_MenuItem"
(
	"GroupCode" VARCHAR(50) NOT NULL , 
    "ItemCode" VARCHAR(50) NOT NULL, 
    "Seq" SMALLINT NULL,
    "Description" VARCHAR(200) NULL, 
    "IsEveryone" BOOLEAN NOT NULL DEFAULT FALSE, 
    "Enabled" BOOLEAN NOT NULL DEFAULT FALSE, 
    CONSTRAINT "PK_Base_Setting_MenuItem" PRIMARY KEY ("GroupCode", "ItemCode")
)