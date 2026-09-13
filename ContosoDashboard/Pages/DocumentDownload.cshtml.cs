using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using ContosoDashboard.Services;

namespace ContosoDashboard.Pages
{
    [Authorize]
    public class DocumentDownloadModel : PageModel
    {
        private readonly IDocumentService _documentService;

        public DocumentDownloadModel(IDocumentService documentService)
        {
            _documentService = documentService;
        }

        public async Task<IActionResult> OnGetAsync(int documentId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var currentUserId))
            {
                return Forbid();
            }

            var result = await _documentService.DownloadAsync(documentId, currentUserId);
            if (result == null)
            {
                return NotFound();
            }

            var (document, content) = result.Value;
            return File(content, document.FileType, document.OriginalFileName);
        }
    }
}
