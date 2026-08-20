import { Stack, Title } from '@mantine/core';
import { flatten, getTree } from '@/lib/categories';
import { getT } from '@/i18n/server';
import { CategoryTree } from './category-tree';

export default async function CategoriesPage() {
  const t = await getT();
  const tree = await getTree();
  const options = flatten(tree);

  return (
    <Stack>
      <div>
        <Title order={2}>{t('categories.title')}</Title>
      </div>

      <CategoryTree
        nodes={tree}
        options={options}
        siteUrl={process.env.SITE_URL ?? 'https://www.deboet.nl'}
      />
    </Stack>
  );
}
