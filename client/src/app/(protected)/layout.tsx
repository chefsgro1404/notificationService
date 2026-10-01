import SiteNav from "@/components/SiteNav";
import { requireAdminPage } from "@/lib/auth/guard";

/** Every page in this group requires a signed-in user with the Admin app role. */
export default async function ProtectedLayout({ children }: { children: React.ReactNode }) {
  const user = await requireAdminPage();

  return (
    <>
      <SiteNav user={{ name: user.name, username: user.username, roles: user.roles }} />
      {children}
    </>
  );
}
