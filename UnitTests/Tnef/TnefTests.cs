//
// TnefTests.cs
//
// Author: Jeffrey Stedfast <jestedfa@microsoft.com>
//
// Copyright (c) 2013-2017 Xamarin Inc. (www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Text;
using System.Linq;

using MimeKit;
using MimeKit.IO;
using MimeKit.Tnef;
using MimeKit.IO.Filters;

using NUnit.Framework;

namespace UnitTests.Tnef {
	[TestFixture]
	public class TnefTests
	{
		static void ExtractRecipientTable (TnefReader reader, MimeMessage message)
		{
			var prop = reader.TnefPropertyReader;
			var chars = new char[1024];
			var buf = new byte[1024];

			// Note: The RecipientTable uses rows of properties...
			while (prop.ReadNextRow ()) {
				InternetAddressList list = null;
				string name = null, addr = null;

				while (prop.ReadNextProperty ()) {
					var type = prop.ValueType;
					object value;

					switch (prop.PropertyTag.Id) {
					case TnefPropertyId.RecipientType:
						int recipientType = prop.ReadValueAsInt32 ();
						switch (recipientType) {
						case 1: list = message.To; break;
						case 2: list = message.Cc; break;
						case 3: list = message.Bcc; break;
						default:
							Assert.Fail ("Invalid recipient type.");
							break;
						}
						//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, recipientType);
						break;
					case TnefPropertyId.TransmitableDisplayName:
						if (string.IsNullOrEmpty (name)) {
							name = prop.ReadValueAsString ();
							//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, name);
						} else {
							//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, prop.ReadValueAsString ());
						}
						break;
					case TnefPropertyId.DisplayName:
						name = prop.ReadValueAsString ();
						//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, name);
						break;
					case TnefPropertyId.EmailAddress:
						if (string.IsNullOrEmpty (addr)) {
							addr = prop.ReadValueAsString ();
							//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, addr);
						} else {
							//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, prop.ReadValueAsString ());
						}
						break;
					case TnefPropertyId.SmtpAddress:
						// The SmtpAddress, if it exists, should take precedence over the EmailAddress
						// (since the SmtpAddress is meant to be used in the RCPT TO command).
						addr = prop.ReadValueAsString ();
						//Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, addr);
						break;
					case TnefPropertyId.Addrtype:
						Assert.AreEqual (typeof (string), type);
						value = prop.ReadValueAsString ();
						break;
					case TnefPropertyId.Rowid:
						Assert.AreEqual (typeof (int), type);
						value = prop.ReadValueAsInt64 ();
						break;
					case TnefPropertyId.SearchKey:
						Assert.AreEqual (typeof (byte[]), type);
						value = prop.ReadValueAsBytes ();
						break;
					case TnefPropertyId.SendRichInfo:
						Assert.AreEqual (typeof (bool), type);
						value = prop.ReadValueAsBoolean ();
						break;
					case TnefPropertyId.DisplayType:
						Assert.AreEqual (typeof (int), type);
						value = prop.ReadValueAsInt16 ();
						break;
					case TnefPropertyId.SendInternetEncoding:
						Assert.AreEqual (typeof (int), type);
						value = prop.ReadValueAsBoolean ();
						break;
					default:
						Assert.Throws<ArgumentNullException> (() => prop.ReadTextValue (null, 0, chars.Length));
						Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadTextValue (chars, -1, chars.Length));
						Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadTextValue (chars, 0, -1));

						Assert.Throws<ArgumentNullException> (() => prop.ReadRawValue (null, 0, buf.Length));
						Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadRawValue (buf, -1, buf.Length));
						Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadRawValue (buf, 0, -1));

						if (type == typeof (int) || type == typeof (long) || type == typeof (bool) || type == typeof (double) || type == typeof (float)) {
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsString ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsGuid ());
						} else if (type == typeof (string)) {
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsBoolean ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsDouble ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsFloat ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsInt16 ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsInt32 ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsInt64 ());
							Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsGuid ());
						}

						value = prop.ReadValue ();
						//Console.WriteLine ("RecipientTable Property (unhandled): {0} = {1}", prop.PropertyTag.Id, value);
						Assert.AreEqual (type, value.GetType (), "Unexpected value type for {0}: {1}", prop.PropertyTag, value.GetType ().Name);
						break;
					}
				}

				Assert.IsNotNull (list, "The recipient type was never specified.");
				Assert.IsNotNull (addr, "The address was never specified.");

				if (list != null)
					list.Add (new MailboxAddress (name, addr));
			}
		}

		static void ExtractMapiProperties (TnefReader reader, MimeMessage message, BodyBuilder builder)
		{
			var prop = reader.TnefPropertyReader;
			var chars = new char[1024];
			var buf = new byte[1024];

			while (prop.ReadNextProperty ()) {
				var type = prop.ValueType;
				object value;

				switch (prop.PropertyTag.Id) {
				case TnefPropertyId.InternetMessageId:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode) {
						message.MessageId = prop.ReadValueAsString ();
						//Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, message.MessageId);
					} else {
						Assert.Fail ("Unknown property type for Message-Id: {0}", prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.Subject:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode) {
						message.Subject = prop.ReadValueAsString ();
						//Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, message.Subject);
					} else {
						Assert.Fail ("Unknown property type for Subject: {0}", prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.RtfCompressed:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Binary) {
						var rtf = new TextPart ("rtf");
						rtf.ContentType.Name = "body.rtf";

						var converter = new RtfCompressedToRtf ();
						converter.Reset ();

						var content = new MemoryStream ();

						using (var filtered = new FilteredStream (content)) {
							filtered.Add (converter);

							using (var compressed = prop.GetRawValueReadStream ()) {
								compressed.CopyTo (filtered, 4096);
								filtered.Flush ();
							}
						}

						rtf.Content = new MimeContent (content);
						content.Position = 0;

						builder.Attachments.Add (rtf);

						//Console.WriteLine ("Message Property: {0} = <compressed rtf data>", prop.PropertyTag.Id);
					} else {
						Assert.Fail ("Unknown property type for {0}: {1}", prop.PropertyTag.Id, prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.BodyHtml:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Binary) {
						var html = new TextPart ("html");
						html.ContentType.Name = "body.html";
						html.Text = prop.ReadValueAsString ();

						builder.Attachments.Add (html);

						//Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, html.Text);
					} else {
						Assert.Fail ("Unknown property type for {0}: {1}", prop.PropertyTag.Id, prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.Body:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Binary) {
						var plain = new TextPart ("plain");
						plain.ContentType.Name = "body.txt";
						plain.Text = prop.ReadValueAsString ();

						builder.Attachments.Add (plain);

						//Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, plain.Text);
					} else {
						Assert.Fail ("Unknown property type for {0}: {1}", prop.PropertyTag.Id, prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.AlternateRecipientAllowed:
					Assert.AreEqual (typeof (bool), type);
					value = prop.ReadValueAsBoolean ();
					break;
				case TnefPropertyId.MessageClass:
					Assert.AreEqual (typeof (string), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.Importance:
					Assert.AreEqual (typeof (int), type);
					value = prop.ReadValueAsInt16 ();
					break;
				case TnefPropertyId.Priority:
					Assert.AreEqual (typeof (int), type);
					value = prop.ReadValueAsInt16 ();
					break;
				case TnefPropertyId.Sensitivity:
					Assert.AreEqual (typeof (int), type);
					value = prop.ReadValueAsInt16 ();
					break;
				case TnefPropertyId.ClientSubmitTime:
					Assert.AreEqual (typeof (DateTime), type);
					value = prop.ReadValueAsDateTime ();
					break;
				case TnefPropertyId.SubjectPrefix:
					Assert.AreEqual (typeof (string), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.MessageSubmissionId:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.ConversationTopic:
					Assert.AreEqual (typeof (string), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.ConversationIndex:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsBytes ();
					break;
				case TnefPropertyId.SenderName:
					Assert.AreEqual (typeof (string), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.NormalizedSubject:
					Assert.AreEqual (typeof (string), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.CreationTime:
					Assert.AreEqual (typeof (DateTime), type);
					value = prop.ReadValueAsDateTime ();
					break;
				case TnefPropertyId.LastModificationTime:
					Assert.AreEqual (typeof (DateTime), type);
					value = prop.ReadValueAsDateTime ();
					break;
				case TnefPropertyId.InternetCPID:
					Assert.AreEqual (typeof (int), type);
					value = prop.ReadValueAsInt32 ();
					break;
				case TnefPropertyId.MessageCodepage:
					Assert.AreEqual (typeof (int), type);
					value = prop.ReadValueAsInt32 ();
					break;
				case TnefPropertyId.INetMailOverrideFormat:
					Assert.AreEqual (typeof (int), type);
					value = prop.ReadValueAsInt32 ();
					break;
				case TnefPropertyId.ReadReceiptRequested:
					Assert.AreEqual (typeof (bool), type);
					value = prop.ReadValueAsBoolean ();
					break;
				case TnefPropertyId.OriginatorDeliveryReportRequested:
					Assert.AreEqual (typeof (bool), type);
					value = prop.ReadValueAsBoolean ();
					break;
				case TnefPropertyId.TnefCorrelationKey:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.SenderSearchKey:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.DeleteAfterSubmit:
					Assert.AreEqual (typeof (bool), type);
					value = prop.ReadValueAsBoolean ();
					break;
				case TnefPropertyId.MessageDeliveryTime:
					Assert.AreEqual (typeof (DateTime), type);
					value = prop.ReadValueAsDateTime ();
					break;
				case TnefPropertyId.SentmailEntryId:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsString ();
					break;
				case TnefPropertyId.RtfInSync:
					Assert.AreEqual (typeof (bool), type);
					value = prop.ReadValueAsBoolean ();
					break;
				case TnefPropertyId.MappingSignature:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsBytes ();
					break;
				case TnefPropertyId.StoreRecordKey:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsBytes ();
					break;
				case TnefPropertyId.StoreEntryId:
					Assert.AreEqual (typeof (byte[]), type);
					value = prop.ReadValueAsBytes ();
					break;
				default:
					Assert.Throws<ArgumentNullException> (() => prop.ReadTextValue (null, 0, chars.Length));
					Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadTextValue (chars, -1, chars.Length));
					Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadTextValue (chars, 0, -1));

					Assert.Throws<ArgumentNullException> (() => prop.ReadRawValue (null, 0, buf.Length));
					Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadRawValue (buf, -1, buf.Length));
					Assert.Throws<ArgumentOutOfRangeException> (() => prop.ReadRawValue (buf, 0, -1));

					if (type == typeof (int) || type == typeof (long) || type == typeof (bool) || type == typeof (double) || type == typeof (float)) {
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsString ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsGuid ());
					} else if (type == typeof (string)) {
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsBoolean ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsDouble ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsFloat ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsInt16 ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsInt32 ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsInt64 ());
						Assert.Throws<InvalidOperationException> (() => prop.ReadValueAsGuid ());
					}

					try {
						value = prop.ReadValue ();
					} catch (Exception ex) {
						Console.WriteLine ("Error in prop.ReadValue(): {0}", ex);
						value = null;
					}

					//Console.WriteLine ("Message Property (unhandled): {0} = {1}", prop.PropertyTag.Id, value);
					Assert.AreEqual (type, value.GetType (), "Unexpected value type for {0}: {1}", prop.PropertyTag, value.GetType ().Name);
					break;
				}
			}
		}

		static void ExtractAttachments (TnefReader reader, BodyBuilder builder)
		{
			var attachMethod = TnefAttachMethod.ByValue;
			var filter = new BestEncodingFilter ();
			var prop = reader.TnefPropertyReader;
			MimePart attachment = null;
			int outIndex, outLength;
			TnefAttachFlags flags;
			string[] mimeType;
			byte[] attachData;
			DateTime time;
			string text;

			//Console.WriteLine ("Extracting attachments...");

			do {
				if (reader.AttributeLevel != TnefAttributeLevel.Attachment)
					Assert.Fail ("Expected attachment attribute level: {0}", reader.AttributeLevel);

				switch (reader.AttributeTag) {
				case TnefAttributeTag.AttachRenderData:
					//Console.WriteLine ("Attachment Attribute: {0}", reader.AttributeTag);
					attachMethod = TnefAttachMethod.ByValue;
					attachment = new MimePart ();
					break;
				case TnefAttributeTag.Attachment:
					//Console.WriteLine ("Attachment Attribute: {0}", reader.AttributeTag);
					if (attachment == null)
						break;

					while (prop.ReadNextProperty ()) {
						switch (prop.PropertyTag.Id) {
						case TnefPropertyId.AttachLongFilename:
							attachment.FileName = prop.ReadValueAsString ();

							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, attachment.FileName);
							break;
						case TnefPropertyId.AttachFilename:
							if (attachment.FileName == null) {
								attachment.FileName = prop.ReadValueAsString ();
								//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, attachment.FileName);
							} else {
								//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, prop.ReadValueAsString ());
							}
							break;
						case TnefPropertyId.AttachContentLocation:
							text = prop.ReadValueAsString ();
							if (Uri.IsWellFormedUriString (text, UriKind.Absolute))
								attachment.ContentLocation = new Uri (text, UriKind.Absolute);
							else if (Uri.IsWellFormedUriString (text, UriKind.Relative))
								attachment.ContentLocation = new Uri (text, UriKind.Relative);
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, text);
							break;
						case TnefPropertyId.AttachContentBase:
							text = prop.ReadValueAsString ();
							attachment.ContentBase = new Uri (text, UriKind.Absolute);
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, text);
							break;
						case TnefPropertyId.AttachContentId:
							attachment.ContentId = prop.ReadValueAsString ();
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, attachment.ContentId);
							break;
						case TnefPropertyId.AttachDisposition:
							text = prop.ReadValueAsString ();
							if (attachment.ContentDisposition == null)
								attachment.ContentDisposition = new ContentDisposition (text);
							else
								attachment.ContentDisposition.Disposition = text;
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, text);
							break;
						case TnefPropertyId.AttachMethod:
							attachMethod = (TnefAttachMethod) prop.ReadValueAsInt32 ();
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, attachMethod);
							break;
						case TnefPropertyId.AttachMimeTag:
							text = prop.ReadValueAsString ();
							mimeType = text.Split ('/');
							if (mimeType.Length == 2) {
								attachment.ContentType.MediaType = mimeType[0].Trim ();
								attachment.ContentType.MediaSubtype = mimeType[1].Trim ();
							}
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, text);
							break;
						case TnefPropertyId.AttachFlags:
							flags = (TnefAttachFlags) prop.ReadValueAsInt32 ();
							if ((flags & TnefAttachFlags.RenderedInBody) != 0) {
								if (attachment.ContentDisposition == null)
									attachment.ContentDisposition = new ContentDisposition (ContentDisposition.Inline);
								else
									attachment.ContentDisposition.Disposition = ContentDisposition.Inline;
							}
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, flags);
							break;
						case TnefPropertyId.AttachData:
							var stream = prop.GetRawValueReadStream ();
							var content = new MemoryStream ();

							if (attachMethod == TnefAttachMethod.EmbeddedMessage) {
								var tnef = new TnefPart ();

								foreach (var param in attachment.ContentType.Parameters)
									tnef.ContentType.Parameters[param.Name] = param.Value;

								if (attachment.ContentDisposition != null)
									tnef.ContentDisposition = attachment.ContentDisposition;

								attachment = tnef;
							}

							stream.CopyTo (content, 4096);

							var buffer = content.GetBuffer ();
							filter.Flush (buffer, 0, (int) content.Length, out outIndex, out outLength);
							attachment.ContentTransferEncoding = filter.GetBestEncoding (EncodingConstraint.SevenBit);
							attachment.Content = new MimeContent (content);
							filter.Reset ();

							//Console.WriteLine ("Attachment Property: {0} has GUID {1}", prop.PropertyTag.Id, new Guid (guid));

							builder.Attachments.Add (attachment);
							break;
						case TnefPropertyId.DisplayName:
							attachment.ContentType.Name = prop.ReadValueAsString ();
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, attachment.ContentType.Name);
							break;
						case TnefPropertyId.AttachSize:
							if (attachment.ContentDisposition == null)
								attachment.ContentDisposition = new ContentDisposition ();

							attachment.ContentDisposition.Size = prop.ReadValueAsInt64 ();
							//Console.WriteLine ("Attachment Property: {0} = {1}", prop.PropertyTag.Id, attachment.ContentDisposition.Size.Value);
							break;
						default:
							//Console.WriteLine ("Attachment Property (unhandled): {0} = {1}", prop.PropertyTag.Id, prop.ReadValue ());
							break;
						}
					}
					break;
				case TnefAttributeTag.AttachData:
					//Console.WriteLine ("Attachment Attribute: {0}", reader.AttributeTag);
					if (attachment == null || attachMethod != TnefAttachMethod.ByValue)
						break;

					attachData = prop.ReadValueAsBytes ();
					filter.Flush (attachData, 0, attachData.Length, out outIndex, out outLength);
					attachment.ContentTransferEncoding = filter.GetBestEncoding (EncodingConstraint.SevenBit);
					attachment.Content = new MimeContent (new MemoryStream (attachData, false));
					filter.Reset ();

					builder.Attachments.Add (attachment);
					break;
				case TnefAttributeTag.AttachCreateDate:
					time = prop.ReadValueAsDateTime ();

					if (attachment != null) {
						if (attachment.ContentDisposition == null)
							attachment.ContentDisposition = new ContentDisposition ();

						attachment.ContentDisposition.CreationDate = time;
					}

					//Console.WriteLine ("Attachment Attribute: {0} = {1}", reader.AttributeTag, time);
					break;
				case TnefAttributeTag.AttachModifyDate:
					time = prop.ReadValueAsDateTime ();

					if (attachment != null) {
						if (attachment.ContentDisposition == null)
							attachment.ContentDisposition = new ContentDisposition ();

						attachment.ContentDisposition.ModificationDate = time;
					}

					//Console.WriteLine ("Attachment Attribute: {0} = {1}", reader.AttributeTag, time);
					break;
				case TnefAttributeTag.AttachTitle:
					text = prop.ReadValueAsString ();

					if (attachment != null && string.IsNullOrEmpty (attachment.FileName))
						attachment.FileName = text;

					//Console.WriteLine ("Attachment Attribute: {0} = {1}", reader.AttributeTag, text);
					break;
				//case TnefAttributeTag.AttachMetaFile:
				//	break;
				default:
					var type = prop.ValueType;
					var value = prop.ReadValue ();
					//Console.WriteLine ("Attachment Attribute (unhandled): {0} = {1}", reader.AttributeTag, value);
					Assert.AreEqual (type, value.GetType (), "Unexpected value type for {0}: {1}", reader.AttributeTag, value.GetType ().Name);
					break;
				}
			} while (reader.ReadNextAttribute ());
		}

		static MimeMessage ExtractTnefMessage (TnefReader reader)
		{
			var builder = new BodyBuilder ();
			var message = new MimeMessage ();

			while (reader.ReadNextAttribute ()) {
				if (reader.AttributeLevel == TnefAttributeLevel.Attachment)
					break;

				if (reader.AttributeLevel != TnefAttributeLevel.Message)
					Assert.Fail ("Unknown attribute level: {0}", reader.AttributeLevel);

				var prop = reader.TnefPropertyReader;

				switch (reader.AttributeTag) {
				case TnefAttributeTag.RecipientTable:
					ExtractRecipientTable (reader, message);
					break;
				case TnefAttributeTag.MapiProperties:
					ExtractMapiProperties (reader, message, builder);
					break;
				case TnefAttributeTag.DateSent:
					message.Date = prop.ReadValueAsDateTime ();
					//Console.WriteLine ("Message Attribute: {0} = {1}", reader.AttributeTag, message.Date);
					break;
				case TnefAttributeTag.Body:
					builder.TextBody = prop.ReadValueAsString ();
					//Console.WriteLine ("Message Attribute: {0} = {1}", reader.AttributeTag, builder.TextBody);
					break;
				case TnefAttributeTag.TnefVersion:
					//Console.WriteLine ("Message Attribute: {0} = {1}", reader.AttributeTag, prop.ReadValueAsInt32 ());
					break;
				case TnefAttributeTag.OemCodepage:
					int codepage = prop.ReadValueAsInt32 ();
					try {
						var encoding = Encoding.GetEncoding (codepage);
						//Console.WriteLine ("Message Attribute: OemCodepage = {0}", encoding.HeaderName);
					} catch {
						//Console.WriteLine ("Message Attribute: OemCodepage = {0}", codepage);
					}
					break;
				default:
					//Console.WriteLine ("Message Attribute (unhandled): {0} = {1}", reader.AttributeTag, prop.ReadValue ());
					break;
				}
			}

			if (reader.AttributeLevel == TnefAttributeLevel.Attachment) {
				ExtractAttachments (reader, builder);
			} else {
				//Console.WriteLine ("no attachments");
			}

			message.Body = builder.ToMessageBody ();

			return message;
		}

		static MimeMessage ParseTnefMessage (string path, TnefComplianceStatus expected)
		{
			using (var reader = new TnefReader (File.OpenRead (path), 0, TnefComplianceMode.Loose)) {
				var message = ExtractTnefMessage (reader);

				Assert.AreEqual (expected, reader.ComplianceStatus, "Unexpected compliance status.");

				return message;
			}
		}

		static void TestTnefParser (string path, TnefComplianceStatus expected = TnefComplianceStatus.Compliant)
		{
			var message = ParseTnefMessage (path + ".tnef", expected);
			var names = File.ReadAllLines (path + ".list");

			foreach (var name in names) {
				bool found = false;

				foreach (var part in message.BodyParts.OfType<MimePart> ()) {
					if (part.FileName == name) {
						found = true;
						break;
					}
				}

				if (!found)
					Assert.Fail ("Failed to locate attachment: {0}", name);
			}

			// now use TnefPart to do the same thing
			using (var content = File.OpenRead (path + ".tnef")) {
				var tnef = new TnefPart { Content = new MimeContent (content) };
				var attachments = tnef.ExtractAttachments ().ToList ();

				foreach (var name in names) {
					bool found = false;

					foreach (var part in attachments.OfType<MimePart> ()) {
						if (part is TextPart && string.IsNullOrEmpty (part.FileName)) {
							var basename = Path.GetFileNameWithoutExtension (name);
							var extension = Path.GetExtension (name);
							string subtype;

							switch (extension) {
							case ".html": subtype = "html"; break;
							case ".rtf": subtype = "rtf"; break;
							default: subtype = "plain"; break;
							}

							if (basename == "body" && part.ContentType.IsMimeType ("text", subtype)) {
								found = true;
								break;
							}
						} else if (part.FileName == name) {
							found = true;
							break;
						}
					}

					if (!found)
						Assert.Fail ("Failed to locate attachment in TnefPart: {0}", name);
				}
			}
		}

		[Test]
		public void TestBody ()
		{
			TestTnefParser ("../../TestData/tnef/body");
		}

		[Test]
		public void TestDataBeforeName ()
		{
			TestTnefParser ("../../TestData/tnef/data-before-name");
		}

		[Test]
		public void TestGarbageAtEnd ()
		{
			const TnefComplianceStatus errors = TnefComplianceStatus.InvalidAttributeLevel | TnefComplianceStatus.StreamTruncated;

			TestTnefParser ("../../TestData/tnef/garbage-at-end", errors);
		}

		[Test]
		public void TestLongFileName ()
		{
			TestTnefParser ("../../TestData/tnef/long-filename");
		}

		[Test]
		public void TestMapiAttachDataObj ()
		{
			TestTnefParser ("../../TestData/tnef/MAPI_ATTACH_DATA_OBJ");
		}

		[Test]
		public void TestMapiObject ()
		{
			TestTnefParser ("../../TestData/tnef/MAPI_OBJECT");
		}

		[Test]
		public void TestMissingFileNames ()
		{
			TestTnefParser ("../../TestData/tnef/missing-filenames");
		}

		[Test]
		public void TestMultiNameProperty ()
		{
			TestTnefParser ("../../TestData/tnef/multi-name-property");
		}

		[Test]
		public void TestMultiValueAttribute ()
		{
			TestTnefParser ("../../TestData/tnef/multi-value-attribute");
		}

		[Test]
		public void TestOneFile ()
		{
			TestTnefParser ("../../TestData/tnef/one-file");
		}

		[Test]
		public void TestRtf ()
		{
			TestTnefParser ("../../TestData/tnef/rtf");
		}

		[Test]
		public void TestTriples ()
		{
			TestTnefParser ("../../TestData/tnef/triples");
		}

		[Test]
		public void TestTwoFiles ()
		{
			TestTnefParser ("../../TestData/tnef/two-files");
		}

		[Test]
		public void TestUnicodeMapiAttrName ()
		{
			TestTnefParser ("../../TestData/tnef/unicode-mapi-attr-name");
		}

		[Test]
		public void TestUnicodeMapiAttr ()
		{
			TestTnefParser ("../../TestData/tnef/unicode-mapi-attr");
		}

		[Test]
		public void TestWinMail ()
		{
			TestTnefParser ("../../TestData/tnef/winmail");
		}

		[Test]
		public void TestExtractedCharset ()
		{
			const string expected = "<html>\r\n<head>\r\n<meta http-equiv=\"Content-Type\" content=\"text/html; charset=koi8-r\">\r\n<style type=\"text/css\" style=\"display:none;\"><!-- P {margin-top:0;margin-bottom:0;} --></style>\r\n</head>\r\n<body dir=\"ltr\">\r\n<div id=\"divtagdefaultwrapper\" style=\"font-size:12pt;color:#000000;font-family:Calibri,Helvetica,sans-serif;\" dir=\"ltr\">\r\n<p>ЫПУФЙК</p>\r\n<p><br>\r\n</p>\r\n<p>{EMAILSIGNATURE}</p>\r\n<p><br>\r\n</p>\r\n<div id=\"Signature\"><br>\r\n<font color=\"#888888\" face=\"Arial, Helvetica, Helvetica, Geneva, Sans-Serif\" style=\"font-size: 10pt;\"><br>\r\n<font color=\"#888888\" face=\"Arial, Helvetica, Helvetica, Geneva, Sans-Serif\" style=\"font-size: 12pt;\"><b>RR Test 1</b></font>\r\n</font>\r\n<p><font color=\"#888888\" face=\"Arial, Helvetica, Helvetica, Geneva, Sans-Serif\" style=\"font-size: 10pt;\">&nbsp;</font></p>\r\n</div>\r\n</div>\r\n</body>\r\n</html>\r\n";
			var message = MimeMessage.Load ("../../TestData/tnef/ukr.eml");
			var tnef = message.BodyParts.OfType<TnefPart> ().FirstOrDefault ();

			message = tnef.ConvertToMessage ();

			Assert.IsInstanceOf (typeof (TextPart), message.Body);

			var text = (TextPart) message.Body;

			Assert.IsTrue (text.IsHtml);

			var html = text.Text;

			Assert.AreEqual ("windows-1251", text.ContentType.Charset);
			Assert.AreEqual (expected.Replace ("\r\n", Environment.NewLine), html);
		}

		[Test]
		public void TestTnefReaderStream ()
		{
			using (var stream = File.OpenRead ("../../TestData/tnef/winmail.tnef")) {
				using (var reader = new TnefReader (stream)) {
					var buffer = new byte[1024];

					using (var tnef = new TnefReaderStream (reader, 0)) {
						Assert.IsTrue (tnef.CanRead);
						Assert.IsFalse (tnef.CanWrite);
						Assert.IsFalse (tnef.CanSeek);
						Assert.IsFalse (tnef.CanTimeout);

						Assert.Throws<ArgumentNullException> (() => tnef.Read (null, 0, buffer.Length));
						Assert.Throws<ArgumentOutOfRangeException> (() => tnef.Read (buffer, -1, buffer.Length));
						Assert.Throws<ArgumentOutOfRangeException> (() => tnef.Read (buffer, 0, -1));

						Assert.Throws<NotSupportedException> (() => tnef.Write (buffer, 0, buffer.Length));
						Assert.Throws<NotSupportedException> (() => tnef.Seek (0, SeekOrigin.End));
						Assert.Throws<NotSupportedException> (() => tnef.Flush ());
						Assert.Throws<NotSupportedException> (() => tnef.SetLength (1024));

						Assert.Throws<NotSupportedException> (() => { var x = tnef.Position; });
						Assert.Throws<NotSupportedException> (() => { tnef.Position = 0; });
						Assert.Throws<NotSupportedException> (() => { var x = tnef.Length; });
					}
				}
			}
		}

		[Test]
		public void TestRtfCompressedToRtfUnknownCompressionType ()
		{
			var input = new byte[] { 0x10, 0x00, 0x00, 0x00, 0x11, 0x00, 0x00, 0x00, (byte) 'A', (byte) 'B', (byte) 'C', (byte) 'D', 0xff, 0xff, 0xff, 0xff };
			var filter = new RtfCompressedToRtf ();
			int outputIndex, outputLength;
			byte[] output;

			output = filter.Flush (input, 0, input.Length, out outputIndex, out outputLength);

			Assert.AreEqual (16, outputIndex, "outputIndex");
			Assert.AreEqual (0, outputLength, "outputLength");
			Assert.IsFalse (filter.IsValidCrc32, "IsValidCrc32");
			Assert.AreEqual ((RtfCompressionMode) 1145258561, filter.CompressionMode, "ComnpressionMode");
		}

		[Test]
		public void TestRtfCompressedToRtfInvalidCrc ()
		{
			var input = new byte[] { 0x10, 0x00, 0x00, 0x00, 0x11, 0x00, 0x00, 0x00, (byte) 'L', (byte) 'Z', (byte) 'F', (byte) 'u', 0xff, 0xff, 0xff, 0xff };
			var filter = new RtfCompressedToRtf ();
			int outputIndex, outputLength;
			byte[] output;

			output = filter.Flush (input, 0, input.Length, out outputIndex, out outputLength);

			Assert.AreEqual (0, outputIndex, "outputIndex");
			Assert.AreEqual (0, outputLength, "outputLength");
			Assert.IsFalse (filter.IsValidCrc32, "IsValidCrc32");
			Assert.AreEqual (RtfCompressionMode.Compressed, filter.CompressionMode, "ComnpressionMode");
		}

		[Test]
		public void TestRtfCompressedToRtf ()
		{
			var input = new byte[] { (byte) '-', 0x00, 0x00, 0x00, (byte) '+', 0x00, 0x00, 0x00, (byte) 'L', (byte) 'Z', (byte) 'F', (byte) 'u', 0xf1, 0xc5, 0xc7, 0xa7, 0x03, 0x00, (byte) '\n', 0x00, (byte) 'r', (byte) 'c', (byte) 'p', (byte) 'g', (byte) '1', (byte) '2', (byte) '5', (byte) 'B', (byte) '2', (byte) '\n', 0xf3, (byte) ' ', (byte) 'h', (byte) 'e', (byte) 'l', (byte) '\t', 0x00, (byte) ' ', (byte) 'b', (byte) 'w', 0x05, 0xb0, (byte) 'l', (byte) 'd', (byte) '}', (byte) '\n', 0x80, 0x0f, 0xa0 };
			const string expected = "{\\rtf1\\ansi\\ansicpg1252\\pard hello world}\r\n";
			var filter = new RtfCompressedToRtf ();
			int outputIndex, outputLength;
			byte[] output;

			output = filter.Flush (input, 0, input.Length, out outputIndex, out outputLength);

			Assert.AreEqual (0, outputIndex, "outputIndex");
			Assert.AreEqual (43, outputLength, "outputLength");
			Assert.IsTrue (filter.IsValidCrc32, "IsValidCrc32");
			Assert.AreEqual (RtfCompressionMode.Compressed, filter.CompressionMode, "ComnpressionMode");

			var text = Encoding.ASCII.GetString (output, outputIndex, outputLength);

			Assert.AreEqual (expected, text);
		}

		[Test]
		public void TestRtfCompressedToRtfByteByByte ()
		{
			var input = new byte[] { (byte) '-', 0x00, 0x00, 0x00, (byte) '+', 0x00, 0x00, 0x00, (byte) 'L', (byte) 'Z', (byte) 'F', (byte) 'u', 0xf1, 0xc5, 0xc7, 0xa7, 0x03, 0x00, (byte) '\n', 0x00, (byte) 'r', (byte) 'c', (byte) 'p', (byte) 'g', (byte) '1', (byte) '2', (byte) '5', (byte) 'B', (byte) '2', (byte) '\n', 0xf3, (byte) ' ', (byte) 'h', (byte) 'e', (byte) 'l', (byte) '\t', 0x00, (byte) ' ', (byte) 'b', (byte) 'w', 0x05, 0xb0, (byte) 'l', (byte) 'd', (byte) '}', (byte) '\n', 0x80, 0x0f, 0xa0 };
			const string expected = "{\\rtf1\\ansi\\ansicpg1252\\pard hello world}\r\n";
			var filter = new RtfCompressedToRtf ();
			int outputIndex, outputLength;
			byte[] output;

			using (var memory = new MemoryStream ()) {
				for (int i = 0; i < input.Length; i++) {
					output = filter.Filter (input, i, 1, out outputIndex, out outputLength);
					memory.Write (output, outputIndex, outputLength);
				}

				output = filter.Flush (input, 0, 0, out outputIndex, out outputLength);
				memory.Write (output, outputIndex, outputLength);

				output = memory.ToArray ();
			}

			Assert.AreEqual (43, output.Length, "outputLength");
			Assert.IsTrue (filter.IsValidCrc32, "IsValidCrc32");
			Assert.AreEqual (RtfCompressionMode.Compressed, filter.CompressionMode, "ComnpressionMode");

			var text = Encoding.ASCII.GetString (output);

			Assert.AreEqual (expected, text);
		}

		[Test]
		public void TestRtfCompressedToRtfRaw ()
		{
			var input = new byte[] { (byte) '.', 0x00, 0x00, 0x00, (byte) '\"', 0x00, 0x00, 0x00, (byte) 'M', (byte) 'E', (byte) 'L', (byte) 'A', (byte) ' ', 0xdf, 0x12, 0xce, (byte) '{', (byte) '\\', (byte) 'r', (byte) 't', (byte) 'f', (byte) '1', (byte) '\\', (byte) 'a', (byte) 'n', (byte) 's', (byte) 'i', (byte) '\\', (byte) 'a', (byte) 'n', (byte) 's', (byte) 'i', (byte) 'c', (byte) 'p', (byte) 'g', (byte) '1', (byte) '2', (byte) '5', (byte) '2', (byte) '\\', (byte) 'p', (byte) 'a', (byte) 'r', (byte) 'd', (byte) ' ', (byte) 't', (byte) 'e', (byte) 's', (byte) 't', (byte) '}' };
			const string expected = "{\\rtf1\\ansi\\ansicpg1252\\pard test}";
			var filter = new RtfCompressedToRtf ();
			int outputIndex, outputLength;
			byte[] output;

			output = filter.Flush (input, 0, input.Length, out outputIndex, out outputLength);

			Assert.AreEqual (16, outputIndex, "outputIndex");
			Assert.AreEqual (34, outputLength, "outputLength");
			Assert.IsTrue (filter.IsValidCrc32, "IsValidCrc32");
			Assert.AreEqual (RtfCompressionMode.Uncompressed, filter.CompressionMode, "ComnpressionMode");

			var text = Encoding.ASCII.GetString (output, outputIndex, outputLength);

			Assert.AreEqual (expected, text);
		}

		[Test]
		public void TestRtfCompressedToRtfRawByteByByte ()
		{
			var input = new byte[] { (byte) '.', 0x00, 0x00, 0x00, (byte) '\"', 0x00, 0x00, 0x00, (byte) 'M', (byte) 'E', (byte) 'L', (byte) 'A', (byte) ' ', 0xdf, 0x12, 0xce, (byte) '{', (byte) '\\', (byte) 'r', (byte) 't', (byte) 'f', (byte) '1', (byte) '\\', (byte) 'a', (byte) 'n', (byte) 's', (byte) 'i', (byte) '\\', (byte) 'a', (byte) 'n', (byte) 's', (byte) 'i', (byte) 'c', (byte) 'p', (byte) 'g', (byte) '1', (byte) '2', (byte) '5', (byte) '2', (byte) '\\', (byte) 'p', (byte) 'a', (byte) 'r', (byte) 'd', (byte) ' ', (byte) 't', (byte) 'e', (byte) 's', (byte) 't', (byte) '}' };
			const string expected = "{\\rtf1\\ansi\\ansicpg1252\\pard test}";
			var filter = new RtfCompressedToRtf ();
			int outputIndex, outputLength;
			byte[] output;

			using (var memory = new MemoryStream ()) {
				for (int i = 0; i < input.Length; i++) {
					output = filter.Filter (input, i, 1, out outputIndex, out outputLength);
					memory.Write (output, outputIndex, outputLength);
				}

				output = filter.Flush (input, 0, 0, out outputIndex, out outputLength);
				memory.Write (output, outputIndex, outputLength);

				output = memory.ToArray ();
			}

			Assert.AreEqual (34, output.Length, "outputLength");
			Assert.IsTrue (filter.IsValidCrc32, "IsValidCrc32");
			Assert.AreEqual (RtfCompressionMode.Uncompressed, filter.CompressionMode, "ComnpressionMode");

			var text = Encoding.ASCII.GetString (output);

			Assert.AreEqual (expected, text);
		}

		[Test]
		public void TestTnefNameId ()
		{
			var guid = Guid.NewGuid ();
			var tnef1 = new TnefNameId (guid, 17);
			var tnef2 = new TnefNameId (guid, 17);

			Assert.AreEqual (TnefNameIdKind.Id, tnef1.Kind, "Kind Id");
			Assert.AreEqual (guid, tnef1.PropertySetGuid, "PropertySetGuid Id");
			Assert.AreEqual (17, tnef1.Id, "Id");

			Assert.AreEqual (tnef1.GetHashCode (), tnef2.GetHashCode (), "GetHashCode Id");
			Assert.AreEqual (tnef1, tnef2, "Equal Id");

			tnef1 = new TnefNameId (guid, "name");
			Assert.AreEqual (TnefNameIdKind.Name, tnef1.Kind, "Kind Name");
			Assert.AreEqual (guid, tnef1.PropertySetGuid, "PropertySetGuid");
			Assert.AreEqual ("name", tnef1.Name, "Name");

			Assert.AreNotEqual (tnef1.GetHashCode (), tnef2.GetHashCode (), "GetHashCode Name vs Id");
			Assert.AreNotEqual (tnef1, tnef2, "Equal Name vs Id");

			tnef2 = new TnefNameId (guid, "name");
			Assert.AreEqual (tnef1.GetHashCode (), tnef2.GetHashCode (), "GetHashCode Name");
			Assert.AreEqual (tnef1, tnef2, "Equal Name");
		}
	}
}
