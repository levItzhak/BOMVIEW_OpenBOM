using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using static BOMVIEW.UserGuideWindow;

namespace BOMVIEW
{
    public partial class UserGuideWindow : Window
    {
        private GuideContent _guideContent;
        private Language _currentLanguage = Language.English;

        public enum Language
        {
            English,
            Hebrew
        }

        public UserGuideWindow()
        {
            InitializeComponent();
            _guideContent = new GuideContent();
            LoadContent();
        }

        private void LoadContent()
        {
            NavigationTree.Items.Clear();
            var sections = _guideContent.GetSections(_currentLanguage);

            foreach (var section in sections)
            {
                var item = new TreeViewItem
                {
                    Header = section.Title,
                    Tag = section
                };

                foreach (var subsection in section.Subsections)
                {
                    var subItem = new TreeViewItem
                    {
                        Header = subsection.Title,
                        Tag = subsection
                    };
                    item.Items.Add(subItem);
                }

                NavigationTree.Items.Add(item);
            }

            if (NavigationTree.Items.Count > 0)
            {
                ((TreeViewItem)NavigationTree.Items[0]).IsSelected = true;
            }

            // Set FlowDirection based on language
            FlowDirection flowDir = _currentLanguage == Language.Hebrew ?
                FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            this.FlowDirection = flowDir;
            NavigationTree.FlowDirection = flowDir;
            ContentPanel.FlowDirection = flowDir;
        }

        private void NavigationTree_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = NavigationTree.SelectedItem as TreeViewItem;
            if (selectedItem?.Tag is GuideSection section)
            {
                ContentTitle.Text = section.Title;
                ContentBody.Text = section.Content;
            }
        }

        private void btnEnglish_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLanguage != Language.English)
            {
                _currentLanguage = Language.English;
                LoadContent();
            }
        }

        private void btnHebrew_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLanguage != Language.Hebrew)
            {
                _currentLanguage = Language.Hebrew;
                LoadContent();
            }
        }
    }

    public class GuideSection
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public List<GuideSection> Subsections { get; set; } = new List<GuideSection>();
    }

    public class GuideContent
    {
        private Dictionary<Language, List<GuideSection>> _content;

        public GuideContent()
        {
            InitializeContent();
        }

        private void InitializeContent()
        {
            _content = new Dictionary<Language, List<GuideSection>>
            {
                { Language.English, CreateEnglishContent() },
                { Language.Hebrew, CreateHebrewContent() }
            };
        }

        public List<GuideSection> GetSections(Language language)
        {
            return _content[language];
        }

        private List<GuideSection> CreateEnglishContent()
        {
            return new List<GuideSection>
            {
                new GuideSection
                {
                    Title = "Getting Started",
                    Content = "Welcome to the BOM Price Comparison tool. This guide will help you understand and use all features effectively.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "Quick Start",
                            Content = "1. Click 'Select BOM File' to load your BOM\n" +
                                    "2. Choose a template or map your columns\n" +
                                    "3. Wait for price data to load\n" +
                                    "4. Compare prices and export results"
                        },
                        new GuideSection
                        {
                            Title = "Interface Overview",
                            Content = "The main window consists of:\n" +
                                    "- Top toolbar with main actions\n" +
                                    "- Central grid showing BOM data\n" +
                                    "- Price comparison columns\n" +
                                    "- Summary totals at bottom"
                        }
                    }
                },
                new GuideSection
                {
                    Title = "Working with Templates",
                    Content = "Templates help you quickly load BOMs with predefined column mappings.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "Creating Templates",
                            Content = "To create a new template:\n" +
                                    "1. Load a BOM file\n" +
                                    "2. Map the columns as needed\n" +
                                    "3. Click 'Save As' in the template dialog\n" +
                                    "4. Enter a template name"
                        },
                        new GuideSection
                        {
                            Title = "Using Quick Load",
                            Content = "Quick Load allows you to:\n" +
                                    "1. Select a predefined template\n" +
                                    "2. Set assembly quantity\n" +
                                    "3. Load your BOM file with one click"
                        }
                    }
                },
                new GuideSection
                {
                    Title = "Price Comparison",
                    Content = "Compare prices between DigiKey and Mouser suppliers.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "Understanding the Grid",
                            Content = "The grid shows:\n" +
                                    "- DigiKey prices (blue columns)\n" +
                                    "- Mouser prices (green columns)\n" +
                                    "- Best price indicators\n" +
                                    "- Stock availability"
                        },
                        new GuideSection
                        {
                            Title = "Managing Missing Products",
                            Content = "For products not found:\n" +
                                    "1. Click 'Missing Products'\n" +
                                    "2. Search alternative part numbers\n" +
                                    "3. Update part numbers as needed\n" +
                                    "4. Refresh price data"
                        }
                    }
                },
                new GuideSection
                {
                    Title = "Exporting",
                    Content = "Export your BOM data in various formats.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "Saving BOM Files",
                            Content = "When saving, you'll get:\n" +
                                    "- Complete price comparison Excel file\n" +
                                    "- DigiKey-specific order list\n" +
                                    "- Mouser-specific order list\n" +
                                    "- Best price compilation"
                        },
                        new GuideSection
                        {
                            Title = "Email Integration",
                            Content = "Send BOM reports via email:\n" +
                                    "1. Click the email button\n" +
                                    "2. Add any notes or comments\n" +
                                    "3. Send to specified recipients"
                        }
                    }
                }
            };
        }

        private List<GuideSection> CreateHebrewContent()
        {
            return new List<GuideSection>
            {
                new GuideSection
                {
                    Title = "צעדים ראשונים",
                    Content = "ברוכים הבאים לכלי השוואת מחירי ה-BOM. מדריך זה יעזור לכם להבין ולהשתמש בכל התכונות ביעילות.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "התחלה מהירה",
                            Content = "1. לחץ על 'בחר קובץ BOM' לטעינת ה-BOM שלך\n" +
                                    "2. בחר תבנית או מפה את העמודות\n" +
                                    "3. המתן לטעינת נתוני המחירים\n" +
                                    "4. השווה מחירים וייצא תוצאות"
                        },
                        new GuideSection
                        {
                            Title = "סקירת ממשק",
                            Content = "החלון הראשי מורכב מ:\n" +
                                    "- סרגל כלים עליון עם פעולות עיקריות\n" +
                                    "- טבלה מרכזית המציגה נתוני BOM\n" +
                                    "- עמודות השוואת מחירים\n" +
                                    "- סיכומים בתחתית"
                        }
                    }
                },
                new GuideSection
                {
                    Title = "עבודה עם תבניות",
                    Content = "תבניות עוזרות לך לטעון BOM במהירות עם מיפויי עמודות מוגדרים מראש.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "יצירת תבניות",
                            Content = "ליצירת תבנית חדשה:\n" +
                                    "1. טען קובץ BOM\n" +
                                    "2. מפה את העמודות כנדרש\n" +
                                    "3. לחץ על 'שמור בשם' בתיבת הדו-שיח של התבנית\n" +
                                    "4. הזן שם לתבנית"
                        },
                        new GuideSection
                        {
                            Title = "שימוש בטעינה מהירה",
                            Content = "טעינה מהירה מאפשרת לך:\n" +
                                    "1. לבחור תבנית מוגדרת מראש\n" +
                                    "2. להגדיר כמות הרכבה\n" +
                                    "3. לטעון את קובץ ה-BOM בלחיצה אחת"
                        }
                    }
                },
                new GuideSection
                {
                    Title = "השוואת מחירים",
                    Content = "השווה מחירים בין ספקי DigiKey ו-Mouser.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "הבנת הטבלה",
                            Content = "הטבלה מציגה:\n" +
                                    "- מחירי DigiKey (עמודות כחולות)\n" +
                                    "- מחירי Mouser (עמודות ירוקות)\n" +
                                    "- מחוונים למחיר הטוב ביותר\n" +
                                    "- זמינות במלאי"
                        },
                        new GuideSection
                        {
                            Title = "ניהול מוצרים חסרים",
                            Content = "עבור מוצרים שלא נמצאו:\n" +
          "1. לחץ על 'מוצרים חסרים'\n" +
          "2. חפש מספרי חלק חלופיים\n" +
          "3. עדכן מספרי חלק לפי הצורך\n" +
          "4. רענן את נתוני המחירים"
                        }
                    }
                },
                new GuideSection
                {
                    Title = "ייצוא",
                    Content = "ייצא את נתוני ה-BOM שלך בפורמטים שונים.",
                    Subsections = new List<GuideSection>
                    {
                        new GuideSection
                        {
                            Title = "שמירת קבצי BOM",
                            Content = "בעת השמירה, תקבל:\n" +
                                    "- קובץ אקסל מלא עם השוואת מחירים\n" +
                                    "- רשימת הזמנה ספציפית ל-DigiKey\n" +
                                    "- רשימת הזמנה ספציפית ל-Mouser\n" +
                                    "- ריכוז המחירים הטובים ביותר"
                        },
                        new GuideSection
                        {
                            Title = "שילוב דואר אלקטרוני",
                            Content = "שלח דוחות BOM באמצעות דואר אלקטרוני:\n" +
                                    "1. לחץ על כפתור הדואר האלקטרוני\n" +
                                    "2. הוסף הערות או הארות\n" +
                                    "3. שלח לתמיכה כדי לתת לו בראש על זה שמשהו לא עובד לך כמו שאתה רוצה"
                        }
                    }
                }
            };
        }
    }
}