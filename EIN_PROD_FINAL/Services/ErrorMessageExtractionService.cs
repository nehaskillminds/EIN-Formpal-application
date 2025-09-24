using EinAutomation.Api.Services.Interfaces;
using OpenQA.Selenium;
using System.Text.RegularExpressions;

namespace EinAutomation.Api.Services
{
    public class ErrorMessageExtractionService : IErrorMessageExtractionService
    {
        public readonly ILogger<ErrorMessageExtractionService> _logger;

        public ErrorMessageExtractionService(ILogger<ErrorMessageExtractionService> logger)
        {
            _logger = logger;
        }

        public string ExtractErrorMessage(IWebDriver driver)
        {
            try
            {
                _logger.LogInformation("Attempting to extract error message from page");

                // First, try to find the new error alert boxes
                var newErrorAlerts = driver.FindElements(By.ClassName("ErrorAlertBox_fixSectionAlert__YrMmr"));
                if (newErrorAlerts.Any())
                {
                    return ExtractErrorFromNewAlertBoxes(newErrorAlerts);
                }

                // Fallback: try to find the error container by ID
                var errorContainer = driver.FindElements(By.Id("errorListId")).FirstOrDefault();
                if (errorContainer != null)
                {
                    return ExtractErrorFromContainer(errorContainer);
                }

                // If not found by ID, try to find by class name
                var errorElements = driver.FindElements(By.ClassName("validation_error_text"));
                if (errorElements.Any())
                {
                    return ExtractErrorFromElements(errorElements);
                }

                // Try to find by partial text match
                var errorByText = driver.FindElements(By.XPath("//*[contains(text(), 'Error(s) has occurred')]"));
                if (errorByText.Any())
                {
                    return ExtractErrorFromErrorSection(errorByText.First());
                }

                // If no error messages found, try scrolling to top and retrying
                _logger.LogWarning("No error messages found on the page. Scrolling to top and retrying...");
                try
                {
                    var jsExecutor = (IJavaScriptExecutor)driver;
                    
                    // More aggressive scroll strategy - try multiple scroll positions
                    _logger.LogInformation("Attempting aggressive scroll strategy for error message visibility...");
                    
                    // Scroll to top first
                    jsExecutor.ExecuteScript("window.scrollTo(0, 0);");
                    Thread.Sleep(1000);
                    _logger.LogInformation("Scrolled to top of page");
                    
                    // Try extraction after scrolling to top
                    var topScrollResult = TryExtractAfterScroll(driver);
                    if (!string.IsNullOrEmpty(topScrollResult))
                    {
                        return topScrollResult;
                    }
                    
                    // If still no result, try scrolling to middle of page
                    jsExecutor.ExecuteScript("window.scrollTo(0, document.body.scrollHeight / 2);");
                    Thread.Sleep(1000);
                    _logger.LogInformation("Scrolled to middle of page");
                    
                    var middleScrollResult = TryExtractAfterScroll(driver);
                    if (!string.IsNullOrEmpty(middleScrollResult))
                    {
                        return middleScrollResult;
                    }
                    
                    // Final attempt - scroll to bottom then back to top
                    jsExecutor.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                    Thread.Sleep(1000);
                    jsExecutor.ExecuteScript("window.scrollTo(0, 0);");
                    Thread.Sleep(1000);
                    _logger.LogInformation("Scrolled to bottom then back to top");
                    
                    var finalScrollResult = TryExtractAfterScroll(driver);
                    if (!string.IsNullOrEmpty(finalScrollResult))
                    {
                        return finalScrollResult;
                    }
                }
                catch (Exception scrollEx)
                {
                    _logger.LogDebug("Scroll strategy failed: {Message}", scrollEx.Message);
                }

                _logger.LogWarning("No error messages found on the page even after aggressive scrolling strategy");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while extracting error message from page");
                return string.Empty;
            }
        }

        private string TryExtractAfterScroll(IWebDriver driver)
        {
            try
            {
                // Try extraction after scroll
                var newErrorAlerts = driver.FindElements(By.ClassName("ErrorAlertBox_fixSectionAlert__YrMmr"));
                if (newErrorAlerts.Any())
                {
                    var result = ExtractErrorFromNewAlertBoxes(newErrorAlerts);
                    if (!string.IsNullOrEmpty(result))
                    {
                        _logger.LogInformation("Successfully extracted error message after scroll: {ErrorMessage}", result);
                        return result;
                    }
                }

                var errorContainer = driver.FindElements(By.Id("errorListId")).FirstOrDefault();
                if (errorContainer != null)
                {
                    return ExtractErrorFromContainer(errorContainer);
                }

                var errorElements = driver.FindElements(By.ClassName("validation_error_text"));
                if (errorElements.Any())
                {
                    return ExtractErrorFromElements(errorElements);
                }

                var errorByText = driver.FindElements(By.XPath("//*[contains(text(), 'Error(s) has occurred')]"));
                if (errorByText.Any())
                {
                    return ExtractErrorFromErrorSection(errorByText.First());
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error during post-scroll extraction: {Message}", ex.Message);
                return string.Empty;
            }
        }

        public string ExtractErrorMessage(string htmlContent)
        {
            try
            {
                _logger.LogInformation("Attempting to extract error message from HTML content");

                if (string.IsNullOrEmpty(htmlContent))
                {
                    _logger.LogWarning("HTML content is null or empty");
                    return string.Empty;
                }

                // First try new error alert box patterns
                var newErrorMessage = ExtractErrorFromNewAlertBoxHtml(htmlContent);
                if (!string.IsNullOrEmpty(newErrorMessage))
                {
                    return newErrorMessage;
                }

                // Fallback to legacy patterns
                var patterns = new[]
                {
                    // Pattern for anchor tags with error messages
                    @"<a[^>]*href=""[^""]*""[^>]*style=""[^""]*color:\s*[`'""]*#990000[`'""]*[^""]*""[^>]*>([^<]+)</a>",
                    
                    // Pattern for li elements with validation_error_text class
                    @"<li[^>]*class=""[^""]*validation_error_text[^""]*""[^>]*>(?:<a[^>]*>)?([^<]+)(?:</a>)?</li>",
                    
                    // Pattern for any element with validation_error_text class (excluding the header)
                    @"<[^>]*class=""[^""]*validation_error_text[^""]*""[^>]*>(?!Error\(s\) has occurred:)([^<]+)</[^>]*>",
                    
                    // Fallback pattern to capture text after "Error(s) has occurred:"
                    @"Error\(s\) has occurred:[^<]*<[^>]*>([^<]+)"
                };

                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(htmlContent, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (matches.Count > 0)
                    {
                        var errorMessages = matches
                            .Cast<Match>()
                            .Select(m => m.Groups[1].Value.Trim())
                            .Where(msg => !string.IsNullOrEmpty(msg) && 
                                         !msg.Equals("Error(s) has occurred:", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (errorMessages.Any())
                        {
                            var combinedMessage = string.Join("; ", errorMessages);
                            _logger.LogInformation("Extracted error message: {ErrorMessage}", combinedMessage);
                            return combinedMessage;
                        }
                    }
                }

                _logger.LogWarning("No error messages found in HTML content");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while extracting error message from HTML content");
                return string.Empty;
            }
        }

        public string ExtractErrorFromContainer(IWebElement errorContainer)
        {
            try
            {
                // Get the parent container that contains all error elements
                var parentElement = errorContainer.FindElement(By.XPath(".."));
                
                // Find all anchor elements with error styling
                var errorLinks = parentElement.FindElements(By.XPath(".//a[contains(@style, '#990000')]"));
                
                if (errorLinks.Any())
                {
                    var errorMessages = errorLinks
                        .Select(link => link.Text.Trim())
                        .Where(text => !string.IsNullOrEmpty(text))
                        .ToList();

                    if (errorMessages.Any())
                    {
                        var combinedMessage = string.Join("; ", errorMessages);
                        _logger.LogInformation("Extracted error message from container: {ErrorMessage}", combinedMessage);
                        return combinedMessage;
                    }
                }

                // Fallback: get all li elements with validation_error_text class
                var errorItems = parentElement.FindElements(By.XPath(".//li[@class='validation_error_text']"));
                if (errorItems.Any())
                {
                    var errorMessages = errorItems
                        .Select(item => item.Text.Trim())
                        .Where(text => !string.IsNullOrEmpty(text))
                        .ToList();

                    if (errorMessages.Any())
                    {
                        var combinedMessage = string.Join("; ", errorMessages);
                        _logger.LogInformation("Extracted error message from list items: {ErrorMessage}", combinedMessage);
                        return combinedMessage;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from error container");
                return string.Empty;
            }
        }

        public string ExtractErrorFromElements(IList<IWebElement> errorElements)
        {
            try
            {
                var errorMessages = new List<string>();

                foreach (var element in errorElements)
                {
                    var text = element.Text.Trim();
                    
                    // Skip the header text
                    if (text.Contains("Error(s) has occurred:"))
                        continue;

                    // Check if this element contains anchor links with error styling
                    var errorLinks = element.FindElements(By.XPath(".//a[contains(@style, '#990000')]"));
                    if (errorLinks.Any())
                    {
                        foreach (var link in errorLinks)
                        {
                            var linkText = link.Text.Trim();
                            if (!string.IsNullOrEmpty(linkText))
                            {
                                errorMessages.Add(linkText);
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(text))
                    {
                        errorMessages.Add(text);
                    }
                }

                if (errorMessages.Any())
                {
                    var combinedMessage = string.Join("; ", errorMessages);
                    _logger.LogInformation("Extracted error message from elements: {ErrorMessage}", combinedMessage);
                    return combinedMessage;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from error elements");
                return string.Empty;
            }
        }

        public string ExtractErrorFromErrorSection(IWebElement errorSection)
        {
            try
            {
                // Get the parent container
                var parentElement = errorSection.FindElement(By.XPath(".."));
                
                // Look for anchor elements with error styling
                var errorLinks = parentElement.FindElements(By.XPath(".//a[contains(@style, '#990000')]"));
                
                if (errorLinks.Any())
                {
                    var errorMessages = errorLinks
                        .Select(link => link.Text.Trim())
                        .Where(text => !string.IsNullOrEmpty(text))
                        .ToList();

                    if (errorMessages.Any())
                    {
                        var combinedMessage = string.Join("; ", errorMessages);
                        _logger.LogInformation("Extracted error message from error section: {ErrorMessage}", combinedMessage);
                        return combinedMessage;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from error section");
                return string.Empty;
            }
        }

        public string ExtractErrorFromNewAlertBoxes(IList<IWebElement> alertBoxes)
        {
            try
            {
                var errorMessages = new List<string>();

                foreach (var alertBox in alertBoxes)
                {
                    // Extract title from section-alert__title
                    var titleElements = alertBox.FindElements(By.ClassName("section-alert__title"));
                    foreach (var titleElement in titleElements)
                    {
                        var titleText = titleElement.Text.Trim();
                        if (!string.IsNullOrEmpty(titleText) && 
                            !titleText.Equals("The following error has occurred:", StringComparison.OrdinalIgnoreCase))
                        {
                            errorMessages.Add(titleText);
                        }
                    }

                    // Extract error messages from ordered/unordered lists
                    var listItems = alertBox.FindElements(By.XPath(".//ol/li | .//ul/li"));
                    foreach (var listItem in listItems)
                    {
                        // Check if list item contains a link with error message
                        var errorLink = listItem.FindElements(By.TagName("a")).FirstOrDefault();
                        if (errorLink != null)
                        {
                            var linkText = errorLink.Text.Trim();
                            if (!string.IsNullOrEmpty(linkText))
                            {
                                errorMessages.Add(linkText);
                            }
                            
                            // Also check aria-label attribute for additional error details
                            var ariaLabel = errorLink.GetAttribute("aria-label");
                            if (!string.IsNullOrEmpty(ariaLabel) && 
                                !ariaLabel.Equals(linkText, StringComparison.OrdinalIgnoreCase))
                            {
                                errorMessages.Add(ariaLabel);
                            }
                        }
                        else
                        {
                            // If no link, get the text content of the list item
                            var itemText = listItem.Text.Trim();
                            if (!string.IsNullOrEmpty(itemText))
                            {
                                errorMessages.Add(itemText);
                            }
                        }
                    }
                    
                    // Also try to find any text content within the alert box that might contain error details
                    var alertText = alertBox.Text;
                    if (!string.IsNullOrEmpty(alertText))
                    {
                        // Look for specific error patterns in the text
                        var errorPatterns = new[]
                        {
                            @"Legal Name:\s*([^\.]+)",
                            @"contains an ending such as\s*'([^']+)'",
                            @"which is not permitted"
                        };
                        
                        foreach (var pattern in errorPatterns)
                        {
                            var matches = Regex.Matches(alertText, pattern, RegexOptions.IgnoreCase);
                            foreach (Match match in matches)
                            {
                                if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                                {
                                    var extractedError = match.Groups[1].Value.Trim();
                                    if (!errorMessages.Contains(extractedError, StringComparer.OrdinalIgnoreCase))
                                    {
                                        errorMessages.Add(extractedError);
                                    }
                                }
                            }
                        }
                    }
                }

                if (errorMessages.Any())
                {
                    var combinedMessage = string.Join("; ", errorMessages);
                    _logger.LogInformation("Extracted error message from new alert boxes: {ErrorMessage}", combinedMessage);
                    return combinedMessage;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from new alert boxes");
                return string.Empty;
            }
        }

        public string ExtractErrorFromNewAlertBoxHtml(string htmlContent)
        {
            try
            {
                var errorMessages = new List<string>();

                // Pattern for new error alert box structure
                var alertBoxPattern = @"<div[^>]*class=""[^""]*ErrorAlertBox_fixSectionAlert__YrMmr[^""]*""[^>]*>(.*?)</div>(?=\s*<div|\s*$)";
                var alertBoxMatches = Regex.Matches(htmlContent, alertBoxPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                foreach (Match alertMatch in alertBoxMatches)
                {
                    var alertContent = alertMatch.Groups[1].Value;

                    // Extract title from section-alert__title (excluding generic titles)
                    var titlePattern = @"<h2[^>]*class=""[^""]*section-alert__title[^""]*""[^>]*>([^<]+)</h2>";
                    var titleMatch = Regex.Match(alertContent, titlePattern, RegexOptions.IgnoreCase);
                    if (titleMatch.Success)
                    {
                        var titleText = titleMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(titleText) && 
                            !titleText.Equals("The following error has occurred:", StringComparison.OrdinalIgnoreCase))
                        {
                            errorMessages.Add(titleText);
                        }
                    }

                    // Extract error messages from list items
                    var listItemPattern = @"<(?:ol|ul)[^>]*>.*?<li[^>]*>(?:<a[^>]*>)?([^<]+)(?:</a>)?</li>.*?</(?:ol|ul)>";
                    var listMatches = Regex.Matches(alertContent, listItemPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    
                    foreach (Match listMatch in listMatches)
                    {
                        var itemText = listMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(itemText))
                        {
                            errorMessages.Add(itemText);
                        }
                    }

                    // Alternative pattern for individual list items
                    var individualItemPattern = @"<li[^>]*>(?:<a[^>]*[^>]*>)?([^<]+)(?:</a>)?</li>";
                    var individualMatches = Regex.Matches(alertContent, individualItemPattern, RegexOptions.IgnoreCase);
                    
                    foreach (Match itemMatch in individualMatches)
                    {
                        var itemText = itemMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(itemText) && !errorMessages.Contains(itemText))
                        {
                            errorMessages.Add(itemText);
                        }
                    }
                }

                if (errorMessages.Any())
                {
                    var combinedMessage = string.Join("; ", errorMessages.Distinct());
                    _logger.LogInformation("Extracted error message from new HTML structure: {ErrorMessage}", combinedMessage);
                    return combinedMessage;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from new alert box HTML");
                return string.Empty;
            }
        }
    }
}