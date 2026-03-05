-- PEX 管理平台 功能選單
MERGE INTO "Base_Setting_MenuGroup" AS "Target"
	USING (VALUES
		('MG-CRM', 5, '客戶關係管理', '2A65844B-13C5-4D17-85EE-3734445A74F5')
	) AS "Source" ("Code", "Seq", "Description", "SystemId")
	ON ("Target"."Code" = "Source"."Code") 
	WHEN NOT MATCHED THEN
	INSERT("Code", "Seq", "Description", "SystemId")
	VALUES("Source"."Code", "Source"."Seq", "Source"."Description", "Source"."SystemId"::UUID);
