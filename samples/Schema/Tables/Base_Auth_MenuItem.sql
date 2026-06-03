CREATE TABLE "Base_Auth_MenuItem"
(
    "RoleCode" VARCHAR(50) NOT NULL,
    "ItemCode" VARCHAR(50) NOT NULL,
    CONSTRAINT "PK_Base_Auth_MenuItem" PRIMARY KEY ("RoleCode", "ItemCode")
)

COMMENT ON TABLE "Base_Auth_MenuItem" IS '角色可使用的功能選單資料表';

COMMENT ON COLUMN "Base_Auth_MenuItem"."RoleCode" IS '角色代碼 Base_Auth_Role.Code';
COMMENT ON COLUMN "Base_Auth_MenuItem"."ItemCode" IS '選單代碼 Base_Setting_MenuItem.Code';