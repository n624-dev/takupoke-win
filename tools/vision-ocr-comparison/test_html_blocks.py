import unittest
import html_blocks


class HtmlProtocolTests(unittest.TestCase):
    def test_trained_markup_is_interpreted_without_text_normalization_or_geometry_credit(self):
        value = html_blocks.decode('\n<div data-bbox="0 1 900 999" data-label="Text">架空<span> I0</span>&amp;&#x41;</div>\n')
        self.assertEqual('架空 I0&A', value['text'])
        self.assertEqual([[0, 1, 900, 999]], value['predictedBoxes'])
        self.assertEqual('ABSENT', value['originalOwnershipProof'])

    def test_native_text_spaces_and_breaks_remain(self):
        self.assertEqual(' 甲\n乙 ', html_blocks.decode('<p> 甲<br>乙 </p>')['text'])

    def test_malformed_or_active_markup_cannot_be_repaired_into_a_proposal(self):
        cases = ['<div>甲', '<div>甲</p>', 'answer<div>甲</div>', '<div>甲</div><div>乙</div>',
                 '<script>甲</script>', '<div style="color:white">甲</div>', '<div onclick="x">甲</div>',
                 '<div data-bbox="0 0 2 2" data-bbox="0 0 3 3">甲</div>',
                 '<div data-bbox="0 0 0 0">甲</div>', '<div data-bbox="0 0 1001 2">甲</div>',
                 '<div data-bbox="[0 0 2 2">甲</div>', '<div data-bbox="0 0 2 2]">甲</div>',
                 '<div>&unknown;</div>', '<div>&#x110000;</div>', '<!-- x --><div>甲</div>']
        for text in cases:
            with self.subTest(text=text), self.assertRaises(html_blocks.Refusal): html_blocks.decode(text)

    def test_length_event_and_depth_budgets_remain_bounded(self):
        for text in ['<div>'+'甲'*6000+'</div>', '<div>'+'<br>'*257+'</div>', '<div>'*17+'甲'+'</div>'*17]:
            with self.assertRaises(html_blocks.Refusal): html_blocks.decode(text)


if __name__ == '__main__': unittest.main()
