MERGE INTO "Base_Setting_Code" AS "Target"
	USING (VALUES    
		-- 系統別代碼
		('2A65844B-13C5-4D17-85EE-3734445A74F5', 'C0000000-0000-0000-0000-000000000001', 'PrimeEagleX', 'PEX 管理平台', 5)
	) AS "Source" ("Id", "TypeId", "Code", "Name", "Seq")
	ON ("Target"."Id" = "Source"."Id"::UUID) 
	WHEN NOT MATCHED THEN
	INSERT("Id", "TypeId", "Code", "Name", "Seq", "CreatedDate")
	VALUES("Source"."Id"::UUID, "Source"."TypeId"::UUID, "Source"."Code", "Source"."Name", "Source"."Seq", CURRENT_TIMESTAMP);
