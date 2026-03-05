-- 功能選單明細
MERGE INTO "Base_Setting_MenuItem" AS "Target"
	USING (VALUES  
		-- 客戶關係管理
		('MG-CRM', 'MP-CRM-Customer', 10, '客戶資料維護', 0, 1),
		('MG-CRM', 'MP-CRM-CustomerContract', 15, '客戶合約管理', 0, 1)
	) AS "Source" ("GroupCode", "ItemCode", "Seq", "Description", "IsEveryone", "Enabled")
	ON ("Target"."GroupCode" = "Source"."GroupCode" and "Target"."ItemCode" = "Source"."ItemCode") 
	WHEN NOT MATCHED THEN
	INSERT("GroupCode", "ItemCode", "Seq", "Description", "IsEveryone", "Enabled")
	VALUES("Source"."GroupCode", "Source"."ItemCode", "Source"."Seq", "Source"."Description", "Source"."IsEveryone"::BOOLEAN, "Source"."Enabled"::BOOLEAN);
