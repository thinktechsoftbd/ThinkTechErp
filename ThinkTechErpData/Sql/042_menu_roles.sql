-- A menu can be limited to several roles (Security -> Menus -> Visible to), not just one.
--
-- public."MenuRole" links a menu to each role that sees it. A menu without any role is shown to every signed-in
-- user; otherwise to users holding any of its roles (users can still be given or denied a menu under Users ->
-- Menu access). Each menu's current single role ("Menu"."roleId") is copied in, so nothing changes on screen.
--
-- "Menu"."roleId" is kept: the API still counts it as one of the menu's roles (so older scripts that set it keep
-- working, and an API without this change keeps working too), and clears it when a menu is saved, as its roles
-- then live here. Idempotent; run on every database that runs this API (thinktecherp, rbmartdb, fccomdb).

BEGIN;

CREATE TABLE IF NOT EXISTS public."MenuRole"
(
    "menuId" INT NOT NULL,
    "roleId" INT NOT NULL,
    CONSTRAINT "MenuRole_pkey" PRIMARY KEY ("menuId", "roleId"),
    CONSTRAINT "MenuRole_menuId_fkey"
        FOREIGN KEY ("menuId") REFERENCES public."Menu" (id) ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT "MenuRole_roleId_fkey"
        FOREIGN KEY ("roleId") REFERENCES public."Role" (id) ON UPDATE CASCADE ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "MenuRole_roleId_idx" ON public."MenuRole" ("roleId");

INSERT INTO public."MenuRole" ("menuId", "roleId")
SELECT id, "roleId" FROM public."Menu" WHERE "roleId" IS NOT NULL
ON CONFLICT DO NOTHING;

SELECT count(*) AS menu_roles, count(DISTINCT "menuId") AS menus_with_roles FROM public."MenuRole";

COMMIT;
