CREATE OR REPLACE VIEW "vw_Auth_MenuItem"
	AS
Select mItem."GroupCode", mGroup."Seq" as "GroupSeq", mItem."ItemCode", mItem."Seq" as "ItemSeq",
	mGroup."Description" as "GroupDesc", mItem."Description" as "ItemDesc",
	mItem."Enabled", mItem."IsEveryone", mGroup."SystemId", code."Name" as "SystemName",
	(Select count(*) From "Base_Auth_MenuItem" sub Where mItem."ItemCode" = sub."ItemCode") as "RoleCount"
From "Base_Setting_MenuItem" mItem
Inner Join "Base_Setting_MenuGroup" mGroup on mItem."GroupCode" = mGroup."Code"
Inner Join "Base_Setting_Code" code on mGroup."SystemId" = code."Id";
